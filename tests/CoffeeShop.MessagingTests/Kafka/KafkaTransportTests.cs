using System.Text;
using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka;

namespace CoffeeShop.MessagingTests.Kafka;

public sealed class KafkaTransportTests
{
    private static readonly Guid MessageId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrderId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Topic_resolver_uses_semantic_event_identity()
    {
        Assert.Equal(
            "coffeeshop.orders.v1",
            KafkaTopicResolver.Resolve<OrderPlacedV1>("coffeeshop"));
        Assert.Equal(
            "coffeeshop.preparation.v1",
            KafkaTopicResolver.Resolve<OrderItemPreparedV1>("coffeeshop"));
    }

    [Fact]
    public void Json_codec_reads_additive_fields_without_changing_version_one()
    {
        var codec = new JsonIntegrationEventCodec();
        var json = """
            {
              "messageId": "11111111-1111-1111-1111-111111111111",
              "eventType": "coffeeshop.order-placed",
              "eventVersion": 1,
              "occurredAtUtc": "2026-08-26T01:02:03+00:00",
              "correlationId": "order-workflow-11111111",
              "causationId": null,
              "futureEnvelopeField": "ignored",
              "payload": {
                "orderId": "22222222-2222-2222-2222-222222222222",
                "futurePayloadField": 42,
                "items": []
              }
            }
            """;

        var envelope = codec.Deserialize<OrderPlacedV1>(Encoding.UTF8.GetBytes(json));

        Assert.Equal(MessageId, envelope.MessageId);
        Assert.Equal(OrderId, envelope.Payload.OrderId);
        Assert.Equal(OrderPlacedV1.EventType, envelope.EventType);
        Assert.Equal(OrderPlacedV1.EventVersion, envelope.EventVersion);
    }

    [Fact]
    public void Kafka_mapper_duplicates_envelope_identity_in_headers()
    {
        var mapper = new KafkaIntegrationEventMapper(new JsonIntegrationEventCodec());
        var envelope = CreateEnvelope(MessageId);

        var message = mapper.ToMessage(OrderId.ToString("D"), envelope);
        var decoded = mapper.FromMessage<OrderPlacedV1>(message);

        Assert.Equal(OrderId.ToString("D"), message.Key);
        Assert.Equal(MessageId, decoded.MessageId);
        Assert.Equal(
            "application/json",
            ReadHeader(message.Headers, KafkaHeaderNames.ContentType));
        Assert.Equal(
            MessageId.ToString("D"),
            ReadHeader(message.Headers, KafkaHeaderNames.MessageId));
        Assert.Equal(
            OrderPlacedV1.EventType,
            ReadHeader(message.Headers, KafkaHeaderNames.EventType));
        Assert.Equal(
            "1",
            ReadHeader(message.Headers, KafkaHeaderNames.EventVersion));
    }

    [Fact]
    public void Kafka_mapper_rejects_header_and_envelope_identity_mismatch()
    {
        var mapper = new KafkaIntegrationEventMapper(new JsonIntegrationEventCodec());
        var message = mapper.ToMessage(OrderId.ToString("D"), CreateEnvelope(MessageId));
        message.Headers.Remove(KafkaHeaderNames.MessageId);
        message.Headers.Add(
            KafkaHeaderNames.MessageId,
            Encoding.UTF8.GetBytes(Guid.Empty.ToString("D")));

        var exception = Assert.Throws<JsonException>(() =>
            mapper.FromMessage<OrderPlacedV1>(message));

        Assert.Equal("Kafka header 'message-id' does not match the envelope.", exception.Message);
    }

    [Fact]
    public void Kafka_mapper_rejects_duplicate_identity_headers()
    {
        var mapper = new KafkaIntegrationEventMapper(new JsonIntegrationEventCodec());
        var envelope = CreateEnvelope(MessageId);
        var message = mapper.ToMessage(OrderId.ToString("D"), envelope);
        message.Headers.Add(
            KafkaHeaderNames.CorrelationId,
            Encoding.UTF8.GetBytes(envelope.CorrelationId));

        var exception = Assert.Throws<JsonException>(() =>
            mapper.FromMessage<OrderPlacedV1>(message));

        Assert.Equal(
            "Kafka header 'correlation-id' must appear exactly once.",
            exception.Message);
    }

    [Fact]
    public void Kafka_identity_scope_propagates_trace_and_sets_direct_causation()
    {
        var mapper = new KafkaIntegrationEventMapper(new JsonIntegrationEventCodec());
        var envelope = CreateEnvelope(MessageId);
        var publicationIdentity = new MessageIdentity(
            envelope.CorrelationId,
            envelope.CausationId,
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            "lesson27=green");
        var message = mapper.ToMessage(
            OrderId.ToString("D"),
            envelope,
            publicationIdentity);
        var accessor = new MessageIdentityAccessor();
        var identityScope = new KafkaMessageIdentityScope(accessor);

        using (identityScope.Push(envelope, message.Headers))
        {
            Assert.Equal(envelope.CorrelationId, accessor.Current.CorrelationId);
            Assert.Equal(envelope.MessageId.ToString("D"), accessor.Current.CausationId);
            Assert.Equal(publicationIdentity.TraceParent, accessor.Current.TraceParent);
            Assert.Equal(publicationIdentity.TraceState, accessor.Current.TraceState);
        }

        Assert.Equal(
            publicationIdentity.TraceParent,
            ReadHeader(message.Headers, KafkaHeaderNames.TraceParent));
        Assert.Equal(
            publicationIdentity.TraceState,
            ReadHeader(message.Headers, KafkaHeaderNames.TraceState));
        Assert.Throws<InvalidOperationException>(() => accessor.Current);
    }

    [Fact]
    public void Kafka_identity_scope_rejects_duplicate_optional_trace_headers()
    {
        var mapper = new KafkaIntegrationEventMapper(new JsonIntegrationEventCodec());
        var envelope = CreateEnvelope(MessageId);
        var identity = new MessageIdentity(
            envelope.CorrelationId,
            envelope.CausationId,
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            null);
        var message = mapper.ToMessage(OrderId.ToString("D"), envelope, identity);
        message.Headers.Add(
            KafkaHeaderNames.TraceParent,
            Encoding.UTF8.GetBytes(identity.TraceParent!));
        var identityScope = new KafkaMessageIdentityScope(new MessageIdentityAccessor());

        var exception = Assert.Throws<JsonException>(() =>
            identityScope.Push(envelope, message.Headers));

        Assert.Equal("Kafka header 'traceparent' must not be duplicated.", exception.Message);
    }

    [Fact]
    public void Kafka_mapper_rejects_outbox_identity_mismatch()
    {
        var mapper = new KafkaIntegrationEventMapper(new JsonIntegrationEventCodec());
        var envelope = CreateEnvelope(MessageId);
        var mismatchedIdentity = new MessageIdentity(
            "33333333-3333-3333-3333-333333333333",
            null,
            null,
            null);

        var exception = Assert.Throws<JsonException>(() => mapper.ToMessage(
            OrderId.ToString("D"),
            envelope,
            mismatchedIdentity));

        Assert.Equal("Kafka publication identity does not match the envelope.", exception.Message);
    }

    private static IntegrationEventEnvelope<OrderPlacedV1> CreateEnvelope(Guid messageId) =>
        new(
            messageId,
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            DateTimeOffset.Parse("2026-08-26T01:02:03+00:00"),
            "order-workflow-11111111",
            null,
            new OrderPlacedV1(OrderId, []));

    private static string ReadHeader(Confluent.Kafka.Headers headers, string name) =>
        Encoding.UTF8.GetString(headers.GetLastBytes(name));
}
