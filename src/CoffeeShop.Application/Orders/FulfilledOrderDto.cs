namespace CoffeeShop.Application.Orders;

public sealed record FulfilledOrderDto(
    Guid Id,
    Guid LoyaltyMemberId,
    string Status,
    IReadOnlyList<FulfilledOrderLineItemDto> LineItems);

public sealed record FulfilledOrderLineItemDto(
    Guid Id,
    string Name,
    decimal Price,
    string Station,
    string Status);
