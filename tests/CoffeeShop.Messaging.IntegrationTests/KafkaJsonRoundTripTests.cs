using System.Threading.Channels;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoffeeShop.Messaging.IntegrationTests;

[Collection(KafkaCollection.Name)]
public sealed class KafkaJsonRoundTripTests(KafkaFixture fixture)
{
    [Fact]
    public async Task Hosted_consumer_survives_topic_creation_race_and_then_handles_message()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = testTimeout.Token;
        var runId = Guid.NewGuid().ToString("N");
        var topicPrefix = $"coffeeshop-{runId}";
        var topic = $"{topicPrefix}.orders.v1";
        var handler = new RecordingOrderPlacedHandler();
        using var host = BuildHost(topicPrefix, $"lesson25-{runId}", handler);

        await host.StartAsync(cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStopped.IsCancellationRequested);

        await CreateTopicAsync(topic);
        var expected = CreateEnvelope(Guid.NewGuid());
        var publisher = host.Services.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(
            expected.Payload.OrderId.ToString("D"),
            expected,
            IdentityFor(expected),
            cancellationToken);

        var received = await handler.ReadAsync(cancellationToken);
        Assert.Equal(expected.MessageId, received.Message.MessageId);

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StopAsync(stopTimeout.Token);
    }

    [Fact]
    public async Task Hosted_consumer_round_trips_json_commits_offset_and_stops_cleanly()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = testTimeout.Token;
        var runId = Guid.NewGuid().ToString("N");
        var topicPrefix = $"coffeeshop-{runId}";
        var groupPrefix = $"lesson22-{runId}";
        var topic = $"{topicPrefix}.orders.v1";
        await CreateTopicAsync(topic);

        var first = CreateEnvelope(Guid.NewGuid());
        var firstHandler = new RecordingOrderPlacedHandler();
        using (var firstHost = BuildHost(topicPrefix, groupPrefix, firstHandler))
        {
            await firstHost.StartAsync();
            var publisher = firstHost.Services.GetRequiredService<IIntegrationEventPublisher>();
            await publisher.PublishAsync(
                first.Payload.OrderId.ToString("D"),
                first,
                IdentityFor(first),
                cancellationToken);

            var received = await firstHandler.ReadAsync(cancellationToken);
            Assert.Equal(first.MessageId, received.Message.MessageId);
            Assert.Equal(first.EventType, received.Message.EventType);
            Assert.Equal(first.EventVersion, received.Message.EventVersion);
            Assert.Equal(first.OccurredAtUtc, received.Message.OccurredAtUtc);
            Assert.Equal(first.CorrelationId, received.Message.CorrelationId);
            Assert.Equal(first.CausationId, received.Message.CausationId);
            Assert.Equal(first.Payload.OrderId, received.Message.Payload.OrderId);
            Assert.Equal(first.Payload.Items.ToArray(), received.Message.Payload.Items.ToArray());
            Assert.Equal("lesson22", received.Context.ConsumerRole);
            Assert.Equal(1, received.Context.DeliveryAttempt);

            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await firstHost.StopAsync(stopTimeout.Token);
        }

        var second = CreateEnvelope(Guid.NewGuid());
        var secondHandler = new RecordingOrderPlacedHandler();
        using (var secondHost = BuildHost(topicPrefix, groupPrefix, secondHandler))
        {
            await secondHost.StartAsync();
            var publisher = secondHost.Services.GetRequiredService<IIntegrationEventPublisher>();
            await publisher.PublishAsync(
                second.Payload.OrderId.ToString("D"),
                second,
                IdentityFor(second),
                cancellationToken);

            var received = await secondHandler.ReadAsync(cancellationToken);
            Assert.Equal(second.MessageId, received.Message.MessageId);

            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await secondHost.StopAsync(stopTimeout.Token);
        }
    }

    [Fact]
    public async Task Hosted_consumer_establishes_message_identity_for_handler()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var cancellationToken = testTimeout.Token;
        var runId = Guid.NewGuid().ToString("N");
        var topicPrefix = $"coffeeshop-{runId}";
        await CreateTopicAsync($"{topicPrefix}.orders.v1");
        using var host = BuildIdentityHost(topicPrefix, $"lesson27-{runId}");
        await host.StartAsync(cancellationToken);
        var envelope = CreateEnvelope(Guid.NewGuid());
        var publicationIdentity = new MessageIdentity(
            envelope.CorrelationId,
            envelope.CausationId,
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            "lesson27=green");
        var publisher = host.Services.GetRequiredService<IIntegrationEventPublisher>();

        await publisher.PublishAsync(
            envelope.Payload.OrderId.ToString("D"),
            envelope,
            publicationIdentity,
            cancellationToken);
        var handler = host.Services.GetRequiredService<IdentityRecordingHandler>();
        var observed = await handler.ReadAsync(cancellationToken);

        Assert.Equal(envelope.CorrelationId, observed.CorrelationId);
        Assert.Equal(envelope.MessageId.ToString("D"), observed.CausationId);
        Assert.Equal(publicationIdentity.TraceParent, observed.TraceParent);
        Assert.Equal(publicationIdentity.TraceState, observed.TraceState);
        await host.StopAsync(CancellationToken.None);
    }

    private IHost BuildHost(
        string topicPrefix,
        string groupPrefix,
        RecordingOrderPlacedHandler handler)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddKafkaMessaging(options =>
        {
            options.BootstrapServers = fixture.BootstrapServers;
            options.TopicPrefix = topicPrefix;
            options.ConsumerGroupPrefix = groupPrefix;
        });
        builder.Services.AddSingleton(handler);
        builder.Services.AddKafkaConsumer<OrderPlacedV1, RecordingOrderPlacedHandler>("lesson22");
        return builder.Build();
    }

    private IHost BuildIdentityHost(string topicPrefix, string groupPrefix)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IMessageIdentityAccessor, MessageIdentityAccessor>();
        builder.Services.AddKafkaMessaging(options =>
        {
            options.BootstrapServers = fixture.BootstrapServers;
            options.TopicPrefix = topicPrefix;
            options.ConsumerGroupPrefix = groupPrefix;
        });
        builder.Services.AddSingleton<IdentityRecordingHandler>();
        builder.Services.AddKafkaConsumer<OrderPlacedV1, IdentityRecordingHandler>("lesson27");
        return builder.Build();
    }

    private async Task CreateTopicAsync(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = fixture.BootstrapServers
        }).Build();
        await admin.CreateTopicsAsync([
            new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1
            }
        ]);
    }

    private static IntegrationEventEnvelope<OrderPlacedV1> CreateEnvelope(Guid messageId) =>
        new(
            messageId,
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            DateTimeOffset.UtcNow,
            $"workflow-{messageId:N}",
            null,
            new OrderPlacedV1(
                Guid.NewGuid(),
                [new OrderLineItemV1(Guid.NewGuid(), "Latte", "Barista")]));

    private static MessageIdentity IdentityFor(
        IntegrationEventEnvelope<OrderPlacedV1> envelope) =>
        new(envelope.CorrelationId, envelope.CausationId, null, null);

    private sealed class RecordingOrderPlacedHandler
        : IIntegrationEventHandler<OrderPlacedV1>
    {
        private readonly Channel<ReceivedMessage> _messages =
            Channel.CreateUnbounded<ReceivedMessage>();

        public async Task HandleAsync(
            IntegrationEventEnvelope<OrderPlacedV1> message,
            IntegrationMessageContext context,
            CancellationToken cancellationToken)
        {
            await _messages.Writer.WriteAsync(
                new ReceivedMessage(message, context),
                cancellationToken);
        }

        public async Task<ReceivedMessage> ReadAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            return await _messages.Reader.ReadAsync(timeout.Token);
        }
    }

    private sealed class IdentityRecordingHandler(IMessageIdentityAccessor identityAccessor)
        : IIntegrationEventHandler<OrderPlacedV1>
    {
        private readonly Channel<MessageIdentity> _messages =
            Channel.CreateUnbounded<MessageIdentity>();

        public async Task HandleAsync(
            IntegrationEventEnvelope<OrderPlacedV1> message,
            IntegrationMessageContext context,
            CancellationToken cancellationToken)
        {
            await _messages.Writer.WriteAsync(identityAccessor.Current, cancellationToken);
        }

        public async Task<MessageIdentity> ReadAsync(CancellationToken cancellationToken) =>
            await _messages.Reader.ReadAsync(cancellationToken);
    }

    private sealed record ReceivedMessage(
        IntegrationEventEnvelope<OrderPlacedV1> Message,
        IntegrationMessageContext Context);
}
