using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Menu;

namespace CoffeeShop.Domain.Orders.Events;

public sealed record OrderItemPrepared(
    Guid OrderId,
    Guid LineItemId,
    ItemType ItemType,
    PreparationStation Station,
    string MadeBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
