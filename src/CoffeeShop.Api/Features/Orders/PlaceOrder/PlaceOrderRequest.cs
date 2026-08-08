namespace CoffeeShop.Api.Features.Orders.PlaceOrder;

public sealed record PlaceOrderRequest(
    int CommandType,
    int OrderSource,
    int Location,
    Guid LoyaltyMemberId,
    IReadOnlyList<PlaceOrderItemRequest> BaristaItems,
    IReadOnlyList<PlaceOrderItemRequest> KitchenItems,
    DateTimeOffset Timestamp);

public sealed record PlaceOrderItemRequest(int ItemType);
