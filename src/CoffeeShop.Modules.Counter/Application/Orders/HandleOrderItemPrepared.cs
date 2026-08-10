using CoffeeShop.Contracts.Orders;
using CoffeeShop.SharedKernel.Domain;
using CoffeeShop.SharedKernel.Events;

namespace CoffeeShop.Modules.Counter.Application.Orders;

internal sealed class HandleOrderItemPrepared(IOrderRepository repository)
    : IDomainEventHandler<OrderItemPrepared>
{
    public async Task HandleAsync(
        OrderItemPrepared prepared,
        CancellationToken cancellationToken)
    {
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
