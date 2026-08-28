using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Kitchen.Application.Outbox;
using CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;

internal sealed class KitchenOutboxWriter(
    KitchenDbContext dbContext,
    IMessageIdentityAccessor identityAccessor)
    : IKitchenOutboxWriter
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

        dbContext.OutboxMessages.Add(new KitchenOutboxMessage(
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
