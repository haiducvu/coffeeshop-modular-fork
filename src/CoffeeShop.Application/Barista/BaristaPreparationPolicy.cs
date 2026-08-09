using CoffeeShop.Domain.Menu;

namespace CoffeeShop.Application.Barista;

public static class BaristaPreparationPolicy
{
    public static TimeSpan GetDelay(ItemType itemType) => itemType switch
    {
        ItemType.CoffeeBlack or ItemType.CoffeeWithRoom => TimeSpan.FromSeconds(5),
        ItemType.Espresso or ItemType.EspressoDouble => TimeSpan.FromSeconds(7),
        ItemType.Cappuccino => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(3)
    };
}
