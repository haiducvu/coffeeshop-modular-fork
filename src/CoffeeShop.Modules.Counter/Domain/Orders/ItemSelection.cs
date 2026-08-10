using CoffeeShop.Contracts.Menu;

namespace CoffeeShop.Modules.Counter.Domain.Orders;

internal sealed record ItemSelection(ItemType ItemType, PreparationStation Station);
