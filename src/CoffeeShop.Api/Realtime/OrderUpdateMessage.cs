namespace CoffeeShop.Api.Realtime;

public sealed record OrderUpdateMessage(
    Guid OrderId,
    Guid LineItemId,
    string ItemType,
    string ItemStatus,
    string OrderStatus,
    string? MadeBy,
    DateTimeOffset OccurredAt);
