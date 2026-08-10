using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Kitchen.Domain;
using CoffeeShop.SharedKernel.Events;
using CoffeeShop.SharedKernel.Time;

namespace CoffeeShop.Modules.Kitchen.Application;

internal sealed class HandleKitchenOrderItemAccepted(
    IKitchenItemRepository repository,
    IPreparationDelay preparationDelay,
    TimeProvider timeProvider) : IDomainEventHandler<OrderItemAccepted>
{
    public async Task HandleAsync(
        OrderItemAccepted accepted,
        CancellationToken cancellationToken)
    {
        if (accepted.Station != PreparationStation.Kitchen)
        {
            return;
        }

        var item = KitchenItem.Accept(
            accepted.OrderId,
            accepted.LineItemId,
            accepted.ItemType,
            timeProvider.GetUtcNow());
        await preparationDelay.DelayAsync(
            KitchenPreparationPolicy.GetDelay(accepted.ItemType),
            cancellationToken);
        item.Complete(timeProvider.GetUtcNow());
        await repository.AddAsync(item, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }
}
