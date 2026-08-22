using CoffeeShop.Domain.Menu;

namespace CoffeeShop.Domain.Orders;

public sealed record ItemSelection(ItemType ItemType, PreparationStation Station);