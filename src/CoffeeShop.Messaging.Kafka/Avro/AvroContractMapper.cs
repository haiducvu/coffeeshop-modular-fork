using System.Globalization;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using AvroOrderItemPreparedV1 = CoffeeShop.Events.V1.OrderItemPreparedV1;
using AvroOrderLineItemV1 = CoffeeShop.Events.V1.OrderLineItemV1;
using AvroOrderPlacedV1 = CoffeeShop.Events.V1.OrderPlacedV1;

namespace CoffeeShop.Messaging.Kafka.Avro;

internal static class AvroContractMapper
{
    internal static AvroOrderPlacedV1 ToAvro(
        IntegrationEventEnvelope<OrderPlacedV1> envelope) =>
        new()
        {
            messageId = envelope.MessageId.ToString("D"),
            eventType = envelope.EventType,
            eventVersion = envelope.EventVersion,
            occurredAtUtc = envelope.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
            correlationId = envelope.CorrelationId,
            causationId = envelope.CausationId,
            orderId = envelope.Payload.OrderId.ToString("D"),
            items = envelope.Payload.Items
                .Select(item => new AvroOrderLineItemV1
                {
                    lineItemId = item.LineItemId.ToString("D"),
                    itemType = item.ItemType,
                    station = item.Station
                })
                .ToList()
        };

    internal static IntegrationEventEnvelope<OrderPlacedV1> FromAvro(
        AvroOrderPlacedV1 message)
    {
        ValidateContract(
            message.eventType,
            message.eventVersion,
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion);
        ArgumentNullException.ThrowIfNull(message.items);

        return new IntegrationEventEnvelope<OrderPlacedV1>(
            Guid.ParseExact(message.messageId, "D"),
            message.eventType,
            message.eventVersion,
            ParseTimestamp(message.occurredAtUtc),
            message.correlationId,
            message.causationId,
            new OrderPlacedV1(
                Guid.ParseExact(message.orderId, "D"),
                message.items.Select(item => new OrderLineItemV1(
                        Guid.ParseExact(item.lineItemId, "D"),
                        item.itemType,
                        item.station))
                    .ToArray()));
    }

    internal static AvroOrderItemPreparedV1 ToAvro(
        IntegrationEventEnvelope<OrderItemPreparedV1> envelope) =>
        new()
        {
            messageId = envelope.MessageId.ToString("D"),
            eventType = envelope.EventType,
            eventVersion = envelope.EventVersion,
            occurredAtUtc = envelope.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
            correlationId = envelope.CorrelationId,
            causationId = envelope.CausationId,
            orderId = envelope.Payload.OrderId.ToString("D"),
            lineItemId = envelope.Payload.LineItemId.ToString("D"),
            itemType = envelope.Payload.ItemType,
            station = envelope.Payload.Station,
            madeBy = envelope.Payload.MadeBy,
            preparedAtUtc = envelope.Payload.OccurredAtUtc.ToString(
                "O",
                CultureInfo.InvariantCulture)
        };

    internal static IntegrationEventEnvelope<OrderItemPreparedV1> FromAvro(
        AvroOrderItemPreparedV1 message)
    {
        ValidateContract(
            message.eventType,
            message.eventVersion,
            OrderItemPreparedV1.EventType,
            OrderItemPreparedV1.EventVersion);

        return new IntegrationEventEnvelope<OrderItemPreparedV1>(
            Guid.ParseExact(message.messageId, "D"),
            message.eventType,
            message.eventVersion,
            ParseTimestamp(message.occurredAtUtc),
            message.correlationId,
            message.causationId,
            new OrderItemPreparedV1(
                Guid.ParseExact(message.orderId, "D"),
                Guid.ParseExact(message.lineItemId, "D"),
                message.itemType,
                message.station,
                message.madeBy,
                ParseTimestamp(message.preparedAtUtc)));
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);

    private static void ValidateContract(
        string eventType,
        int eventVersion,
        string expectedEventType,
        int expectedEventVersion)
    {
        if (!string.Equals(eventType, expectedEventType, StringComparison.Ordinal)
            || eventVersion != expectedEventVersion)
        {
            throw new NotSupportedException(
                $"Avro contract '{eventType}' version '{eventVersion}' is not supported.");
        }
    }
}
