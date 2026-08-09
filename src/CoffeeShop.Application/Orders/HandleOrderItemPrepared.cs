using CoffeeShop.Application.Common.Events;
using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Orders.Events;
using MediatR;

namespace CoffeeShop.Application.Orders;

public sealed class HandleOrderItemPrepared(IOrderRepository repository)
    : INotificationHandler<DomainEventNotification<OrderItemPrepared>>
{
    public async Task Handle(
        DomainEventNotification<OrderItemPrepared> notification,
        CancellationToken cancellationToken)
    {
        var prepared = notification.DomainEvent;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var order = await repository.FindAsync(prepared.OrderId, cancellationToken)
                ?? throw new DomainException($"Order {prepared.OrderId} was not found.");
            var changed = order.CompleteItem(
                prepared.LineItemId,
                prepared.MadeBy,
                prepared.OccurredAt);
            if (!changed)
            {
                return;
            }

            try
            {
                await repository.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (OrderConcurrencyException) when (attempt < 3)
            {
            }
        }
    }
}
