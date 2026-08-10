using CoffeeShop.Contracts.Menu;

namespace CoffeeShop.Modules.Barista.Application;

internal static class BaristaPreparationPolicy
{
    public static TimeSpan GetDelay(ItemType itemType) => itemType switch
    {
        ItemType.CoffeeBlack or ItemType.CoffeeWithRoom => TimeSpan.FromSeconds(5),
        ItemType.Espresso or ItemType.EspressoDouble => TimeSpan.FromSeconds(7),
        ItemType.Cappuccino => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(3)
    };
}
