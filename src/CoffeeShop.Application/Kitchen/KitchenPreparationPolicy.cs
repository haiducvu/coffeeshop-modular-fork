using CoffeeShop.Domain.Menu;

namespace CoffeeShop.Application.Kitchen;

public static class KitchenPreparationPolicy
{
    public static TimeSpan GetDelay(ItemType itemType) => itemType switch
    {
        ItemType.CakePop => TimeSpan.FromSeconds(5),
        ItemType.Croissant or ItemType.CroissantChocolate or ItemType.Muffin =>
            TimeSpan.FromSeconds(7),
        _ => TimeSpan.FromSeconds(3)
    };
}
