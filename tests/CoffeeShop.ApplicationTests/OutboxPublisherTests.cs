using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Counter.Infrastructure.Outbox;
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

    private static ClaimedOutboxMessage CreateClaimedMessage()
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
            null,
            null);
    }

    private sealed class RecordingOutboxStore(IReadOnlyList<ClaimedOutboxMessage> messages)
        : ICounterOutboxStore
    {
        public Guid LeaseId { get; private set; }
        public List<PublishedCall> Published { get; } = [];
        public List<FailedCall> Failed { get; } = [];

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
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public Exception? Failure { get; init; }
        public List<PublishedMessage> Messages { get; } = [];

        public Task PublishAsync<TPayload>(
            string key,
            IntegrationEventEnvelope<TPayload> message,
            CancellationToken cancellationToken)
            where TPayload : IIntegrationEvent
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            var typed = Assert.IsType<IntegrationEventEnvelope<OrderPlacedV1>>(message);
            Messages.Add(new PublishedMessage(key, typed));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record PublishedMessage(
        string Key,
        IntegrationEventEnvelope<OrderPlacedV1> Envelope);

    private sealed record PublishedCall(
        Guid MessageId,
        Guid LeaseId,
        DateTimeOffset PublishedAtUtc);

    private sealed record FailedCall(
        Guid MessageId,
        Guid LeaseId,
        string SafeErrorCode,
        DateTimeOffset NextAttemptAtUtc);
}
