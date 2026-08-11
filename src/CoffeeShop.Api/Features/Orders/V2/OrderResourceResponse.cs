namespace CoffeeShop.Api.Features.Orders.V2;

public sealed record OrderResourceResponse(
    Guid OrderId,
    string Status,
    OrderResourceLinks Links);

public sealed record OrderResourceLinks(string Self);
