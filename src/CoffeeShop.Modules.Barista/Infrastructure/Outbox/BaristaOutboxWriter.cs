using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Barista.Application.Outbox;
using CoffeeShop.Modules.Barista.Infrastructure.Persistence;

namespace CoffeeShop.Modules.Barista.Infrastructure.Outbox;

internal sealed class BaristaOutboxWriter(
    BaristaDbContext dbContext,
    IMessageIdentityAccessor identityAccessor)
    : IBaristaOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false
        };

    public void Enqueue(
        OrderItemPreparedV1 payload,
        DateTimeOffset occurredAtUtc)
    {
        var messageId = Guid.NewGuid();
        var identity = identityAccessor.Current;
        var envelope = new IntegrationEventEnvelope<OrderItemPreparedV1>(
            messageId,
            OrderItemPreparedV1.EventType,
            OrderItemPreparedV1.EventVersion,
            occurredAtUtc,
            identity.CorrelationId,
            identity.CausationId,
            payload);

        dbContext.OutboxMessages.Add(new BaristaOutboxMessage(
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
