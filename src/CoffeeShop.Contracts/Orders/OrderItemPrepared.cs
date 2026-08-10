using CoffeeShop.Contracts.Menu;
using CoffeeShop.SharedKernel.Events;

namespace CoffeeShop.Contracts.Orders;

public sealed record OrderItemPrepared(
    Guid OrderId,
    Guid LineItemId,
    ItemType ItemType,
    PreparationStation Station,
    string MadeBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
