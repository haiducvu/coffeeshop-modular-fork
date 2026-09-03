using System.Diagnostics;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Dapr;
using CoffeeShop.Messaging.Kafka;
using Microsoft.Extensions.Options;

namespace CoffeeShop.MessagingTests.Adapters;

[Collection(MessagingTelemetryCollection.Name)]
public sealed class MessagingAdapterContractTests
{
    private static readonly Guid MessageId =
        Guid.Parse("30111111-1111-1111-1111-111111111111");
    private static readonly Guid OrderId =
        Guid.Parse("30222222-2222-2222-2222-222222222222");
    private static readonly MessageIdentity Identity = new(
        "30333333-3333-3333-3333-333333333333",
        null,
        "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
        "lesson30=green");

    [Fact]
    public async Task Kafka_and_Dapr_publish_the_same_semantic_topic()
    {
        var client = new RecordingDaprPubSubClient();
        var publisher = CreatePublisher(client);
        var envelope = CreateOrderPlaced();

        await publisher.PublishAsync(
            OrderId.ToString("D"),
            envelope,
            Identity,
            CancellationToken.None);

        var publication = Assert.Single(client.Publications);
        Assert.Equal(
            KafkaTopicResolver.Resolve<OrderPlacedV1>("coffeeshop"),
            publication.TopicName);
        Assert.Equal("coffeeshop-pubsub", publication.PubSubName);
        Assert.Same(envelope, publication.Data);
    }

    [Fact]
    public async Task Dapr_publication_preserves_partition_and_envelope_identity()
    {
        var client = new RecordingDaprPubSubClient();
        var publisher = CreatePublisher(client);

        await publisher.PublishAsync(
            OrderId.ToString("D"),
            CreateOrderPlaced(),
            Identity,
            CancellationToken.None);

        var metadata = Assert.Single(client.Publications).Metadata;
        Assert.Equal(OrderId.ToString("D"), metadata["partitionKey"]);
        Assert.Equal(MessageId.ToString("D"), metadata["cloudevent.id"]);
        Assert.Equal(OrderPlacedV1.EventType, metadata["cloudevent.type"]);
        Assert.Equal("coffeeshop", metadata["cloudevent.source"]);
        Assert.Equal(Identity.CorrelationId, metadata["cloudevent.correlationid"]);
        Assert.Equal(Identity.TraceParent, metadata["cloudevent.traceparent"]);
        Assert.Equal(Identity.TraceState, metadata["cloudevent.tracestate"]);
        Assert.DoesNotContain("cloudevent.causationid", metadata.Keys);
    }

    [Fact]
    public async Task Dapr_publisher_emits_a_Dapr_producer_activity()
    {
        Activity? stopped = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped = activity
        };
        ActivitySource.AddActivityListener(listener);
        var publisher = CreatePublisher(new RecordingDaprPubSubClient());

        await publisher.PublishAsync(
            OrderId.ToString("D"),
            CreateOrderPlaced(),
            Identity,
            CancellationToken.None);

        Assert.NotNull(stopped);
        Assert.Equal(ActivityKind.Producer, stopped.Kind);
        Assert.Equal("dapr", Tag(stopped, "messaging.system"));
        Assert.Equal("coffeeshop.orders.v1", Tag(stopped, "messaging.destination.name"));
    }

    [Fact]
    public async Task Dapr_publisher_propagates_cancellation_to_the_sidecar_client()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var client = new RecordingDaprPubSubClient
        {
            Failure = new OperationCanceledException(cancellation.Token)
        };
        var publisher = CreatePublisher(client);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            publisher.PublishAsync(
                OrderId.ToString("D"),
                CreateOrderPlaced(),
                Identity,
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(cancellation.Token, client.ObservedCancellationToken);
    }

    private static DaprIntegrationEventPublisher CreatePublisher(
        IDaprPubSubClient client) => new(
            Options.Create(new DaprMessagingOptions
            {
                PubSubName = "coffeeshop-pubsub",
                TopicPrefix = "coffeeshop"
            }),
            client);

    private static IntegrationEventEnvelope<OrderPlacedV1> CreateOrderPlaced() => new(
        MessageId,
        OrderPlacedV1.EventType,
        OrderPlacedV1.EventVersion,
        DateTimeOffset.Parse("2026-09-02T00:00:00+00:00"),
        Identity.CorrelationId,
        Identity.CausationId,
        new OrderPlacedV1(OrderId, []));

    private static object? Tag(Activity activity, string name) =>
        activity.TagObjects.Single(tag => tag.Key == name).Value;

    private sealed class RecordingDaprPubSubClient : IDaprPubSubClient
    {
        public List<Publication> Publications { get; } = [];
        public Exception? Failure { get; init; }
        public CancellationToken ObservedCancellationToken { get; private set; }

        public Task PublishEventAsync<TPayload>(
            string pubSubName,
            string topicName,
            IntegrationEventEnvelope<TPayload> data,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken)
            where TPayload : IIntegrationEvent
        {
            ObservedCancellationToken = cancellationToken;
            Publications.Add(new Publication(pubSubName, topicName, data, metadata));
            return Failure is null
                ? Task.CompletedTask
                : Task.FromException(Failure);
        }
    }

    private sealed record Publication(
        string PubSubName,
        string TopicName,
        object Data,
        IReadOnlyDictionary<string, string> Metadata);
}
