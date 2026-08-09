using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders;
using MediatR;

namespace CoffeeShop.Application.Orders.PlaceOrder;

public sealed class PlaceOrderHandler(IOrderRepository repository)
    : IRequestHandler<PlaceOrderCommand, PlaceOrderResult>
{
    public async Task<PlaceOrderResult> Handle(
        PlaceOrderCommand request,
        CancellationToken cancellationToken)
    {
        var baristaItems = request.BaristaItems.Select(item =>
            new ItemSelection((ItemType)item.ItemType, PreparationStation.Barista));
        var kitchenItems = request.KitchenItems.Select(item =>
            new ItemSelection((ItemType)item.ItemType, PreparationStation.Kitchen));
        var order = Order.Place(
            (OrderSource)request.OrderSource,
            (Location)request.Location,
            request.LoyaltyMemberId,
            baristaItems.Concat(kitchenItems).ToArray());

        await repository.AddAsync(order, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return new PlaceOrderResult(order.Id);
    }
}
