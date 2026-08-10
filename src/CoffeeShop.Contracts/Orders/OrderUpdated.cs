using CoffeeShop.Contracts.Menu;
using CoffeeShop.SharedKernel.Events;

namespace CoffeeShop.Contracts.Orders;

public sealed record OrderUpdated(
    Guid OrderId,
    Guid LineItemId,
    ItemType ItemType,
    ItemStatus ItemStatus,
    OrderStatus OrderStatus,
    string MadeBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
