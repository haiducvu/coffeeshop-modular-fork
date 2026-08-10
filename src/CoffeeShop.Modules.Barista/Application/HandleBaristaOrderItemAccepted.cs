using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Barista.Domain;
using CoffeeShop.SharedKernel.Events;
using CoffeeShop.SharedKernel.Time;

namespace CoffeeShop.Modules.Barista.Application;

internal sealed class HandleBaristaOrderItemAccepted(
    IBaristaItemRepository repository,
    IPreparationDelay preparationDelay,
    TimeProvider timeProvider) : IDomainEventHandler<OrderItemAccepted>
{
    public async Task HandleAsync(
        OrderItemAccepted accepted,
        CancellationToken cancellationToken)
    {
        if (accepted.Station != PreparationStation.Barista)
        {
            return;
        }

        var item = BaristaItem.Accept(
            accepted.OrderId,
            accepted.LineItemId,
            accepted.ItemType,
            timeProvider.GetUtcNow());
        await preparationDelay.DelayAsync(
            BaristaPreparationPolicy.GetDelay(accepted.ItemType),
            cancellationToken);
        item.Complete(timeProvider.GetUtcNow());
        await repository.AddAsync(item, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }
}
