using CoffeeShop.Application.Common.Events;
using CoffeeShop.Domain.Orders;
using CoffeeShop.Domain.Orders.Events;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace CoffeeShop.Api.Realtime;

public sealed class SignalROrderUpdatePublisher(
    IHubContext<OrderUpdatesHub, IOrderUpdatesClient> hubContext,
    TimeProvider timeProvider)
    : INotificationHandler<DomainEventNotification<OrderItemAccepted>>,
      INotificationHandler<DomainEventNotification<OrderUpdated>>
{
    public Task Handle(
        DomainEventNotification<OrderItemAccepted> notification,
        CancellationToken cancellationToken)
    {
        var accepted = notification.DomainEvent;
        return hubContext.Clients.All.ReceiveOrderUpdate(new OrderUpdateMessage(
            accepted.OrderId,
            accepted.LineItemId,
            accepted.ItemType.ToString(),
            ItemStatus.InProgress.ToString(),
            OrderStatus.InProgress.ToString(),
            null,
            timeProvider.GetUtcNow()));
    }

    public Task Handle(
        DomainEventNotification<OrderUpdated> notification,
        CancellationToken cancellationToken)
    {
        var updated = notification.DomainEvent;
        return hubContext.Clients.All.ReceiveOrderUpdate(new OrderUpdateMessage(
            updated.OrderId,
            updated.LineItemId,
            updated.ItemType.ToString(),
            updated.ItemStatus.ToString(),
            updated.OrderStatus.ToString(),
            updated.MadeBy,
            updated.OccurredAt));
    }
}
