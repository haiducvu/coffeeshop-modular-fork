using CoffeeShop.Contracts.Menu;

namespace CoffeeShop.Modules.Counter.Domain.Menu;

internal sealed record MenuItem(
    ItemType Type,
    string Name,
    decimal Price,
    PreparationStation Station);
