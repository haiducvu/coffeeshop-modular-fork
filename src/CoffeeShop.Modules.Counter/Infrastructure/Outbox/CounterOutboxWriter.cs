using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Counter.Application.Outbox;
using CoffeeShop.Modules.Counter.Infrastructure.Persistence;

namespace CoffeeShop.Modules.Counter.Infrastructure.Outbox;

internal sealed class CounterOutboxWriter(
    CounterDbContext dbContext,
    IMessageIdentityAccessor identityAccessor)
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
        var identity = identityAccessor.Current;
        var envelope = new IntegrationEventEnvelope<OrderPlacedV1>(
            messageId,
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            occurredAtUtc,
            identity.CorrelationId,
            identity.CausationId,
            payload);

        dbContext.OutboxMessages.Add(new CounterOutboxMessage(
            messageId,
            envelope.EventType,
            envelope.EventVersion,
            JsonSerializer.Serialize(envelope, SerializerOptions),
            envelope.OccurredAtUtc,
            envelope.CorrelationId,
            envelope.CausationId,
            identity.TraceParent,
            identity.TraceState));
    }
}
