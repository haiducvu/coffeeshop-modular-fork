using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Menu;

namespace CoffeeShop.Domain.Orders.Events;

public sealed record OrderUpdated(
    Guid OrderId,
    Guid LineItemId,
    ItemType ItemType,
    ItemStatus ItemStatus,
    OrderStatus OrderStatus,
    string MadeBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
