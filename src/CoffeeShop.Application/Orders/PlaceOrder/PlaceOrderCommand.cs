using MediatR;

namespace CoffeeShop.Application.Orders.PlaceOrder;

public sealed record PlaceOrderCommand(
    int OrderSource,
    int Location,
    Guid LoyaltyMemberId,
    IReadOnlyList<PlaceOrderItem> BaristaItems,
    IReadOnlyList<PlaceOrderItem> KitchenItems) : IRequest<PlaceOrderResult>;

public sealed record PlaceOrderItem(int ItemType);

public sealed record PlaceOrderResult(Guid OrderId);
