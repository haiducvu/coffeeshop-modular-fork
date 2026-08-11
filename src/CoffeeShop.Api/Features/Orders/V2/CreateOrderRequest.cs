namespace CoffeeShop.Api.Features.Orders.V2;

public sealed record CreateOrderRequest(
    int OrderSource,
    int Location,
    Guid LoyaltyMemberId,
    IReadOnlyList<int> BaristaItems,
    IReadOnlyList<int> KitchenItems);
