using System.Text;
using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
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
