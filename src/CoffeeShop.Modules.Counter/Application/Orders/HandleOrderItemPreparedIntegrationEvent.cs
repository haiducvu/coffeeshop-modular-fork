using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Counter.Application.Inbox;
using CoffeeShop.SharedKernel.Domain;

namespace CoffeeShop.Modules.Counter.Application.Orders;

internal sealed class HandleOrderItemPreparedIntegrationEvent(
    ICounterInbox inbox,
    IOrderRepository repository,
    TimeProvider timeProvider) : IIntegrationEventHandler<OrderItemPreparedV1>
{
    private const string HandlerName = "counter.order-item-prepared.v1";

    public async Task HandleAsync(
        IntegrationEventEnvelope<OrderItemPreparedV1> message,
        IntegrationMessageContext context,
        CancellationToken cancellationToken)
    {
        var decision = await inbox.BeginAsync(
            HandlerName,
            message.MessageId,
            message.EventType,
            message.EventVersion,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (decision == InboxDecision.Duplicate)
        {
            return;
        }

        var prepared = message.Payload;
        var order = await repository.FindAsync(prepared.OrderId, cancellationToken)
            ?? throw new DomainException($"Order {prepared.OrderId} was not found.");
        order.CompleteItem(
            prepared.LineItemId,
            prepared.MadeBy,
            prepared.OccurredAtUtc);
        await inbox.CompleteAsync(
            HandlerName,
            message.MessageId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
