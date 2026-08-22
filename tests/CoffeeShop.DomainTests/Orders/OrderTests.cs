using CoffeeShop.Domain;
using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders;

namespace CoffeeShop.DomainTests.Orders;

public sealed class OrderTests
{
    [Fact]
    public void Place_uses_the_server_owned_catalog_price()
    {
        var order = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.NewGuid(),
            [new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista)]);
        
        var lineItem = Assert.Single(order.LineItems);
        Assert.Equal(4.50m, lineItem.Price);
        Assert.Equal("CAPPUCCINO", lineItem.Name);
        Assert.Equal(ItemStatus.InProgress, lineItem.Status);
    }

    [Fact]
    public void Place_rejects_an_empty_order()
    {
        Assert.Throws<DomainException>(() =>
            Order.Place(OrderSource.Counter, Location.Atlanta, Guid.NewGuid(), []));
    }

    [Fact]
    public void Place_rejects_an_item_sent_to_the_wrong_station()
    {
        Assert.Throws<DomainException>(() =>
            Order.Place(
                OrderSource.Web,
                Location.Raleigh,
                Guid.NewGuid(),
                [new ItemSelection(ItemType.Croissant, PreparationStation.Barista)]));
    }
}