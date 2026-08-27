using System.Diagnostics;
using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Modules.Kitchen.Application.Outbox;
using CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;

internal sealed class KitchenOutboxWriter(KitchenDbContext dbContext)
    : IKitchenOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false
        };

    public void Enqueue(
        OrderItemPreparedV1 payload,
        DateTimeOffset occurredAtUtc,
        string correlationId,
        string causationId)
    {
        var messageId = Guid.NewGuid();
        var envelope = new IntegrationEventEnvelope<OrderItemPreparedV1>(
            messageId,
            OrderItemPreparedV1.EventType,
            OrderItemPreparedV1.EventVersion,
            occurredAtUtc,
            correlationId,
            causationId,
            payload);
        var activity = Activity.Current;
        var traceParent = activity?.IdFormat == ActivityIdFormat.W3C
            ? activity.Id
            : null;

        dbContext.OutboxMessages.Add(new KitchenOutboxMessage(
            messageId,
            envelope.EventType,
            envelope.EventVersion,
            JsonSerializer.Serialize(envelope, SerializerOptions),
            envelope.OccurredAtUtc,
            envelope.CorrelationId,
            envelope.CausationId,
            traceParent,
            activity?.TraceStateString));
    }
}
