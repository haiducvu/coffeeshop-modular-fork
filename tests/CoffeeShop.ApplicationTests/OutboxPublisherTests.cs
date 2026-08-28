using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Barista.Infrastructure.Outbox;
using CoffeeShop.Modules.Counter.Infrastructure.Outbox;
using CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeeShop.ApplicationTests;

public sealed class OutboxPublisherTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-27T02:03:04+00:00");

    [Fact]
    public async Task Successful_publish_marks_the_same_message_and_lease()
    {
        var message = CreateClaimedMessage();
        var store = new RecordingOutboxStore([message]);
        var transport = new RecordingIntegrationEventPublisher();
        var publisher = CreatePublisher(store, transport);

        var claimed = await publisher.PublishBatchAsync(CancellationToken.None);

        Assert.Equal(1, claimed);
        var published = Assert.Single(transport.Messages);
        Assert.Equal(message.MessageId, published.Envelope.MessageId);
        Assert.Equal(published.Envelope.Payload.OrderId.ToString("D"), published.Key);
        Assert.Equal(message.CorrelationId, published.Identity.CorrelationId);
        Assert.Equal(message.CausationId, published.Identity.CausationId);
        Assert.Equal(message.TraceParent, published.Identity.TraceParent);
        Assert.Equal(message.TraceState, published.Identity.TraceState);
        var marked = Assert.Single(store.Published);
        Assert.Equal(message.MessageId, marked.MessageId);
        Assert.Equal(store.LeaseId, marked.LeaseId);
        Assert.Equal(Now, marked.PublishedAtUtc);
        Assert.Empty(store.Failed);
    }

    [Fact]
    public async Task Transport_failure_releases_the_lease_and_schedules_a_safe_retry()
    {
        var message = CreateClaimedMessage();
        var store = new RecordingOutboxStore([message]);
        var transport = new RecordingIntegrationEventPublisher
        {
            Failure = new InvalidOperationException("secret broker details")
        };
        var publisher = CreatePublisher(store, transport);

        var claimed = await publisher.PublishBatchAsync(CancellationToken.None);

        Assert.Equal(1, claimed);
        Assert.Empty(store.Published);
        var failed = Assert.Single(store.Failed);
        Assert.Equal(message.MessageId, failed.MessageId);
        Assert.Equal(store.LeaseId, failed.LeaseId);
        Assert.Equal("publish-failed", failed.SafeErrorCode);
        Assert.Equal(Now.AddSeconds(5), failed.NextAttemptAtUtc);
        Assert.DoesNotContain("secret", failed.SafeErrorCode, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Contract_corruption_is_quarantined_without_transport_retry()
    {
        var message = CreateClaimedMessage(
            rowCorrelationId: "33333333-3333-3333-3333-333333333333");
        var store = new RecordingOutboxStore([message]);
        var transport = new RecordingIntegrationEventPublisher();
        var publisher = CreatePublisher(store, transport);

        var claimed = await publisher.PublishBatchAsync(CancellationToken.None);

        Assert.Equal(1, claimed);
        Assert.Empty(transport.Messages);
        Assert.Empty(store.Published);
        Assert.Empty(store.Failed);
        var rejected = Assert.Single(store.Rejected);
        Assert.Equal(message.MessageId, rejected.MessageId);
        Assert.Equal(store.LeaseId, rejected.LeaseId);
        Assert.Equal("invalid-contract", rejected.SafeErrorCode);
        Assert.Equal(Now, rejected.RejectedAtUtc);
    }

    [Fact]
    public void Barista_envelope_contract_must_match_the_claimed_row()
    {
        var message = CreatePreparedMessage("corrupt-event-type", station: "Barista");

        Assert.Throws<JsonException>(() => BaristaOutboxPublisher.Deserialize(
            new ClaimedBaristaOutboxMessage(
                message.MessageId,
                OrderItemPreparedV1.EventType,
                OrderItemPreparedV1.EventVersion,
                message.EnvelopeJson,
                message.CorrelationId,
                message.CausationId,
                message.TraceParent,
                message.TraceState)));
    }

    [Fact]
    public void Kitchen_envelope_contract_must_match_the_claimed_row()
    {
        var message = CreatePreparedMessage("corrupt-event-type", station: "Kitchen");

        Assert.Throws<JsonException>(() => KitchenOutboxPublisher.Deserialize(
            new ClaimedKitchenOutboxMessage(
                message.MessageId,
                OrderItemPreparedV1.EventType,
                OrderItemPreparedV1.EventVersion,
                message.EnvelopeJson,
                message.CorrelationId,
                message.CausationId,
                message.TraceParent,
                message.TraceState)));
    }

    private static CounterOutboxPublisher CreatePublisher(
        ICounterOutboxStore store,
        IIntegrationEventPublisher transport) =>
        new(
            store,
            transport,
            Options.Create(new CounterOutboxOptions
            {
                BatchSize = 10,
                LeaseDuration = TimeSpan.FromSeconds(30),
                RetryDelay = TimeSpan.FromSeconds(5),
                PollInterval = TimeSpan.FromSeconds(1)
            }),
            new FixedTimeProvider(Now),
            NullLogger<CounterOutboxPublisher>.Instance);

    private static ClaimedOutboxMessage CreateClaimedMessage(string? rowCorrelationId = null)
    {
        var messageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var orderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var envelope = new IntegrationEventEnvelope<OrderPlacedV1>(
            messageId,
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            Now,
            messageId.ToString("D"),
            null,
            new OrderPlacedV1(orderId, []));
        return new ClaimedOutboxMessage(
            messageId,
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            rowCorrelationId ?? envelope.CorrelationId,
            envelope.CausationId,
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            "lesson27=green");
    }

    private static ClaimedOutboxMessage CreatePreparedMessage(
        string envelopeEventType,
        string station)
    {
        var messageId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var envelope = new IntegrationEventEnvelope<OrderItemPreparedV1>(
            messageId,
            envelopeEventType,
            OrderItemPreparedV1.EventVersion,
            Now,
            "55555555-5555-5555-5555-555555555555",
            "11111111-1111-1111-1111-111111111111",
            new OrderItemPreparedV1(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Coffee",
                station,
                "Lesson 27",
                Now));
        return new ClaimedOutboxMessage(
            messageId,
            OrderItemPreparedV1.EventType,
            OrderItemPreparedV1.EventVersion,
            JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            envelope.CorrelationId,
            envelope.CausationId,
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            "lesson27=green");
    }

    private sealed class RecordingOutboxStore(IReadOnlyList<ClaimedOutboxMessage> messages)
        : ICounterOutboxStore
    {
        public Guid LeaseId { get; private set; }
        public List<PublishedCall> Published { get; } = [];
        public List<FailedCall> Failed { get; } = [];
        public List<RejectedCall> Rejected { get; } = [];

        public Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimBatchAsync(
            Guid leaseId,
            int batchSize,
            DateTimeOffset now,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken)
        {
            LeaseId = leaseId;
            return Task.FromResult(messages);
        }

        public Task MarkPublishedAsync(
            Guid messageId,
            Guid leaseId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Published.Add(new PublishedCall(messageId, leaseId, now));
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            Guid messageId,
            Guid leaseId,
            string safeErrorCode,
            DateTimeOffset nextAttemptAt,
            CancellationToken cancellationToken)
        {
            Failed.Add(new FailedCall(messageId, leaseId, safeErrorCode, nextAttemptAt));
            return Task.CompletedTask;
        }

        public Task MarkRejectedAsync(
            Guid messageId,
            Guid leaseId,
            string safeErrorCode,
            DateTimeOffset rejectedAtUtc,
            CancellationToken cancellationToken)
        {
            Rejected.Add(new RejectedCall(
                messageId,
                leaseId,
                safeErrorCode,
                rejectedAtUtc));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public Exception? Failure { get; init; }
        public List<PublishedMessage> Messages { get; } = [];

        public Task PublishAsync<TPayload>(
            string key,
            IntegrationEventEnvelope<TPayload> message,
            MessageIdentity identity,
            CancellationToken cancellationToken)
            where TPayload : IIntegrationEvent
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            var typed = Assert.IsType<IntegrationEventEnvelope<OrderPlacedV1>>(message);
            Messages.Add(new PublishedMessage(key, typed, identity));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record PublishedMessage(
        string Key,
        IntegrationEventEnvelope<OrderPlacedV1> Envelope,
        MessageIdentity Identity);

    private sealed record PublishedCall(
        Guid MessageId,
        Guid LeaseId,
        DateTimeOffset PublishedAtUtc);

    private sealed record FailedCall(
        Guid MessageId,
        Guid LeaseId,
        string SafeErrorCode,
        DateTimeOffset NextAttemptAtUtc);

    private sealed record RejectedCall(
        Guid MessageId,
        Guid LeaseId,
        string SafeErrorCode,
        DateTimeOffset RejectedAtUtc);
}
