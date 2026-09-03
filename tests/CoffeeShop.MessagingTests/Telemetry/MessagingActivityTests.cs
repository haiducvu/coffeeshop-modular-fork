using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka;

namespace CoffeeShop.MessagingTests.Telemetry;

[Collection(MessagingTelemetryCollection.Name)]
public sealed class MessagingActivityTests
{
    private const string TraceParent =
        "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    [Fact]
    public void Producer_and_consumer_activities_continue_the_persisted_trace()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);
        var identity = new MessageIdentity(
            "29111111-1111-1111-1111-111111111111",
            null,
            TraceParent,
            "lesson29=green");
        var messageId = Guid.Parse("29222222-2222-2222-2222-222222222222");

        string producerTraceParent;
        ActivitySpanId producerSpanId;
        using (var producer = MessagingTelemetry.StartProducerActivity(
                   "kafka",
                   "coffeeshop.orders.v1",
                   "coffeeshop.order-placed",
                   messageId,
                   identity))
        {
            Assert.NotNull(producer);
            Assert.Equal(ActivityKind.Producer, producer.Kind);
            Assert.Equal(
                ActivityTraceId.CreateFromString("4bf92f3577b34da6a3ce929d0e0e4736"),
                producer.TraceId);
            Assert.Equal(
                ActivitySpanId.CreateFromString("00f067aa0ba902b7"),
                producer.ParentSpanId);
            Assert.Equal("coffeeshop.orders.v1", Tag(producer, "messaging.destination.name"));
            Assert.Equal("coffeeshop.order-placed", Tag(producer, "event.type"));
            Assert.Equal(messageId.ToString("D"), Tag(producer, "messaging.message.id"));
            Assert.Equal(identity.CorrelationId, Tag(producer, "business.correlation.id"));
            producerTraceParent = producer.Id!;
            producerSpanId = producer.SpanId;
        }

        using (var consumer = MessagingTelemetry.StartConsumerActivity(
                   "kafka",
                   "coffeeshop.orders.v1",
                   "coffeeshop.order-placed",
                   "barista",
                   deliveryAttempt: 1,
                   messageId,
                   identity.CorrelationId,
                   producerTraceParent,
                   "lesson29=green"))
        {
            Assert.NotNull(consumer);
            Assert.Equal(ActivityKind.Consumer, consumer.Kind);
            Assert.Equal(producerSpanId, consumer.ParentSpanId);
            Assert.Equal("barista", Tag(consumer, "messaging.consumer.group.name"));
            Assert.Equal(1, Tag(consumer, "messaging.delivery.attempt"));
        }

        Assert.Collection(
            stopped,
            activity => Assert.Equal(ActivityKind.Producer, activity.Kind),
            activity => Assert.Equal(ActivityKind.Consumer, activity.Kind));
    }

    [Fact]
    public void Consumer_identity_snapshots_the_consumer_span_for_the_next_outbox()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        var messageId = Guid.Parse("29333333-3333-3333-3333-333333333333");
        var correlationId = "29444444-4444-4444-4444-444444444444";
        var envelope = new IntegrationEventEnvelope<OrderPlacedV1>(
            messageId,
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"),
            correlationId,
            null,
            new OrderPlacedV1(Guid.Parse("29555555-5555-5555-5555-555555555555"), []));
        var headers = new Headers
        {
            { KafkaHeaderNames.TraceParent, Encoding.UTF8.GetBytes(TraceParent) },
            { KafkaHeaderNames.TraceState, Encoding.UTF8.GetBytes("lesson29=green") }
        };
        var accessor = new MessageIdentityAccessor();
        var identityScope = new KafkaMessageIdentityScope(accessor);

        using var consumer = MessagingTelemetry.StartConsumerActivity(
            "kafka",
            "coffeeshop.orders.v1",
            OrderPlacedV1.EventType,
            "barista",
            1,
            messageId,
            correlationId,
            TraceParent,
            "lesson29=green");
        using (identityScope.Push(envelope, headers))
        {
            Assert.Equal(consumer!.Id, accessor.Current.TraceParent);
            Assert.Equal(consumer.TraceStateString, accessor.Current.TraceState);
            Assert.Equal(messageId.ToString("D"), accessor.Current.CausationId);
        }
    }

    private static object? Tag(Activity activity, string name) =>
        activity.TagObjects.Single(tag => tag.Key == name).Value;
}
