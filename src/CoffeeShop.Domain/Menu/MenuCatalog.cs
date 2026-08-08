namespace CoffeeShop.Domain.Menu;

public static class MenuCatalog
{
    public static MenuItem Get(ItemType itemType) => itemType switch
    {
        ItemType.Cappuccino => new(itemType, "CAPPUCCINO", 4.50m, PreparationStation.Barista),
        ItemType.CoffeeBlack => new(itemType, "COFFEE_BLACK", 3.00m, PreparationStation.Barista),
        ItemType.CoffeeWithRoom => new(itemType, "COFFEE_WITH_ROOM", 3.00m, PreparationStation.Barista),
        ItemType.Espresso => new(itemType, "ESPRESSO", 3.50m, PreparationStation.Barista),
        ItemType.EspressoDouble => new(itemType, "ESPRESSO_DOUBLE", 4.50m, PreparationStation.Barista),
        ItemType.Latte => new(itemType, "LATTE", 4.50m, PreparationStation.Barista),
        ItemType.CakePop => new(itemType, "CAKEPOP", 2.50m, PreparationStation.Kitchen),
        ItemType.Croissant => new(itemType, "CROISSANT", 3.25m, PreparationStation.Kitchen),
        ItemType.Muffin => new(itemType, "MUFFIN", 3.00m, PreparationStation.Kitchen),
        ItemType.CroissantChocolate => new(itemType, "CROISSANT_CHOCOLATE", 3.50m, PreparationStation.Kitchen),
        _ => throw new ArgumentOutOfRangeException(nameof(itemType), itemType, "Unknown menu item.")
    };
}
