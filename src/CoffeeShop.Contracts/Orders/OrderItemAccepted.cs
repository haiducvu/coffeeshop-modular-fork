using CoffeeShop.Contracts.Menu;
using CoffeeShop.SharedKernel.Events;

namespace CoffeeShop.Contracts.Orders;

public sealed record OrderItemAccepted(
    Guid OrderId,
    Guid LineItemId,
    ItemType ItemType,
    PreparationStation Station) : IDomainEvent;
