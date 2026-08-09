using CoffeeShop.Application.Common.Events;
using CoffeeShop.Domain.Barista;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders.Events;
using MediatR;

namespace CoffeeShop.Application.Barista;

public sealed class HandleBaristaOrderItemAccepted(
    IBaristaItemRepository repository,
    IPreparationDelay preparationDelay,
    TimeProvider timeProvider)
    : INotificationHandler<DomainEventNotification<OrderItemAccepted>>
{
    public async Task Handle(
        DomainEventNotification<OrderItemAccepted> notification,
        CancellationToken cancellationToken)
    {
        var accepted = notification.DomainEvent;
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
