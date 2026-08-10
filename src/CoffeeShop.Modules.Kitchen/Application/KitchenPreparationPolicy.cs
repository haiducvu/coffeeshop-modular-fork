using CoffeeShop.Contracts.Menu;

namespace CoffeeShop.Modules.Kitchen.Application;

internal static class KitchenPreparationPolicy
{
    public static TimeSpan GetDelay(ItemType itemType) => itemType switch
    {
        ItemType.CakePop => TimeSpan.FromSeconds(5),
        ItemType.Croissant or ItemType.CroissantChocolate or ItemType.Muffin =>
            TimeSpan.FromSeconds(7),
        _ => TimeSpan.FromSeconds(3)
    };
}
