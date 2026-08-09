using CoffeeShop.Application.Common.Events;
using CoffeeShop.Application.Common.Time;
using CoffeeShop.Domain.Kitchen;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders.Events;
using MediatR;

namespace CoffeeShop.Application.Kitchen;

public sealed class HandleKitchenOrderItemAccepted(
    IKitchenItemRepository repository,
    IPreparationDelay preparationDelay,
    TimeProvider timeProvider)
    : INotificationHandler<DomainEventNotification<OrderItemAccepted>>
{
    public async Task Handle(
        DomainEventNotification<OrderItemAccepted> notification,
        CancellationToken cancellationToken)
    {
        var accepted = notification.DomainEvent;
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
