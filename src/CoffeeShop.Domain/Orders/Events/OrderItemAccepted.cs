using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Menu;

namespace CoffeeShop.Domain.Orders.Events;

public sealed record OrderItemAccepted(
    Guid OrderId,
    Guid LineItemId,
    ItemType ItemType,
    PreparationStation Station) : IDomainEvent;
