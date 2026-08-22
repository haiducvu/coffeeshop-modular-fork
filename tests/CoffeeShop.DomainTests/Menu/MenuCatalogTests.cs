using System.Globalization;
using CoffeeShop.Domain.Menu;

namespace CoffeeShop.DomainTests.Menu;

public sealed class MenuCatalogTests
{
    public static TheoryData<ItemType, string, PreparationStation> Menu => new ()
    {
        { ItemType.Cappuccino, "4.50", PreparationStation.Barista },
        { ItemType.CoffeeBlack, "3.00", PreparationStation.Barista },
        { ItemType.CoffeeWithRoom, "3.00", PreparationStation.Barista },
        { ItemType.Espresso, "3.50", PreparationStation.Barista },
        { ItemType.EspressoDouble, "4.50", PreparationStation.Barista },
        { ItemType.Latte, "4.50", PreparationStation.Barista },
        { ItemType.CakePop, "2.50", PreparationStation.Kitchen },
        { ItemType.Croissant, "3.25", PreparationStation.Kitchen },
        { ItemType.Muffin, "3.00", PreparationStation.Kitchen },
        { ItemType.CroissantChocolate, "3.50", PreparationStation.Kitchen }
    };

    [Theory]
    [MemberData(nameof(Menu))]
    public void Get_returns_the_original_price_and_station(
        ItemType itemType, string expectedPrice, PreparationStation expectedStation)
    {
        var item = MenuCatalog.Get(itemType);
        
        Assert.Equal(decimal.Parse(expectedPrice, CultureInfo.InvariantCulture), item.Price);
        Assert.Equal(expectedStation, item.Station);
    }

    [Fact]
    public void Get_rejects_an_unknown_item_instead_of_falling_back_to_cappuccino()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MenuCatalog.Get((ItemType)999));
    }
}