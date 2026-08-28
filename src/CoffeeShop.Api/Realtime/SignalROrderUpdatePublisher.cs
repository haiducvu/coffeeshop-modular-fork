using CoffeeShop.Contracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.SharedKernel.Events;
using Microsoft.AspNetCore.SignalR;

namespace CoffeeShop.Api.Realtime;

public sealed class SignalROrderUpdatePublisher(
    IHubContext<OrderUpdatesHub, IOrderUpdatesClient> hubContext,
    TimeProvider timeProvider,
    IMessageIdentityAccessor identityAccessor)
    : IDomainEventHandler<OrderItemAccepted>,
      IDomainEventHandler<OrderUpdated>
{
    public Task HandleAsync(
        OrderItemAccepted accepted,
        CancellationToken cancellationToken)
    {
        var identity = identityAccessor.Current;
        return hubContext.Clients.All.ReceiveOrderUpdate(new OrderUpdateMessage(
            accepted.OrderId,
            accepted.LineItemId,
            accepted.ItemType.ToString(),
            ItemStatus.InProgress.ToString(),
            OrderStatus.InProgress.ToString(),
            null,
            timeProvider.GetUtcNow(),
            identity.CorrelationId,
            identity.CausationId));
    }

    public Task HandleAsync(
        OrderUpdated updated,
        CancellationToken cancellationToken)
    {
        var identity = identityAccessor.Current;
        return hubContext.Clients.All.ReceiveOrderUpdate(new OrderUpdateMessage(
            updated.OrderId,
            updated.LineItemId,
            updated.ItemType.ToString(),
            updated.ItemStatus.ToString(),
            updated.OrderStatus.ToString(),
            updated.MadeBy,
            updated.OccurredAt,
            identity.CorrelationId,
            identity.CausationId));
    }
}
