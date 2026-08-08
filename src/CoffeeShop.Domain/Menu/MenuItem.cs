namespace CoffeeShop.Domain.Menu;

public sealed record MenuItem(
    ItemType Type,
    string Name,
    decimal Price,
    PreparationStation Station);
