using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Kafka.Avro;

namespace CoffeeShop.MessagingTests.Avro;

public sealed class AvroMappingTests
{
    [Fact]
    public void Order_placed_mapping_preserves_the_broker_neutral_contract()
    {
        var envelope = new IntegrationEventEnvelope<OrderPlacedV1>(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            DateTimeOffset.Parse("2026-08-26T01:02:03+00:00"),
            "order-workflow-11111111",
            null,
            new OrderPlacedV1(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                [
                    new OrderLineItemV1(
                        Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        "Latte",
                        "Barista")
                ]));

        var avro = AvroContractMapper.ToAvro(envelope);

        Assert.Equal("11111111-1111-1111-1111-111111111111", avro.messageId);
        Assert.Equal("coffeeshop.order-placed", avro.eventType);
        Assert.Equal(1, avro.eventVersion);
        Assert.Equal("2026-08-26T01:02:03.0000000+00:00", avro.occurredAtUtc);
        Assert.Equal("order-workflow-11111111", avro.correlationId);
        Assert.Null(avro.causationId);
        Assert.Equal("22222222-2222-2222-2222-222222222222", avro.orderId);
        var item = Assert.Single(avro.items);
        Assert.Equal("33333333-3333-3333-3333-333333333333", item.lineItemId);
        Assert.Equal("Latte", item.itemType);
        Assert.Equal("Barista", item.station);

        var restored = AvroContractMapper.FromAvro(avro);

        Assert.Equal(envelope.MessageId, restored.MessageId);
        Assert.Equal(envelope.OccurredAtUtc, restored.OccurredAtUtc);
        Assert.Equal(envelope.CorrelationId, restored.CorrelationId);
        Assert.Null(restored.CausationId);
        Assert.Equal(envelope.Payload.OrderId, restored.Payload.OrderId);
        var restoredItem = Assert.Single(restored.Payload.Items);
        Assert.Equal(envelope.Payload.Items[0], restoredItem);
    }

    [Fact]
    public void Prepared_item_mapping_preserves_causation_and_completion_data()
    {
        var envelope = new IntegrationEventEnvelope<OrderItemPreparedV1>(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            OrderItemPreparedV1.EventType,
            OrderItemPreparedV1.EventVersion,
            DateTimeOffset.Parse("2026-08-26T01:02:08+00:00"),
            "order-workflow-11111111",
            "11111111-1111-1111-1111-111111111111",
            new OrderItemPreparedV1(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Latte",
                "Barista",
                "barista",
                DateTimeOffset.Parse("2026-08-26T01:02:08+00:00")));

        var avro = AvroContractMapper.ToAvro(envelope);

        Assert.Equal("55555555-5555-5555-5555-555555555555", avro.messageId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", avro.causationId);
        Assert.Equal("33333333-3333-3333-3333-333333333333", avro.lineItemId);
        Assert.Equal("barista", avro.madeBy);
        Assert.Equal("2026-08-26T01:02:08.0000000+00:00", avro.preparedAtUtc);

        var restored = AvroContractMapper.FromAvro(avro);

        Assert.Equal(envelope.MessageId, restored.MessageId);
        Assert.Equal(envelope.CausationId, restored.CausationId);
        Assert.Equal(envelope.Payload, restored.Payload);
    }
}
