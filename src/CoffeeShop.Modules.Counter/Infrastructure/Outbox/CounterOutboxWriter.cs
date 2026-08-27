using System.Diagnostics;
using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Modules.Counter.Application.Outbox;
using CoffeeShop.Modules.Counter.Infrastructure.Persistence;

namespace CoffeeShop.Modules.Counter.Infrastructure.Outbox;

internal sealed class CounterOutboxWriter(CounterDbContext dbContext)
    : ICounterOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false
        };

    public void Enqueue(OrderPlacedV1 payload, DateTimeOffset occurredAtUtc)
    {
        var messageId = Guid.NewGuid();
        var correlationId = messageId.ToString("D");
        var envelope = new IntegrationEventEnvelope<OrderPlacedV1>(
            messageId,
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            occurredAtUtc,
            correlationId,
            null,
            payload);
        var activity = Activity.Current;
        var traceParent = activity?.IdFormat == ActivityIdFormat.W3C
            ? activity.Id
            : null;

        dbContext.OutboxMessages.Add(new CounterOutboxMessage(
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
