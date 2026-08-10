using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Counter.Domain.Orders;

namespace CoffeeShop.DomainTests.Orders;

public sealed class OrderEventTests
{
    [Fact]
    public void Place_raises_one_accepted_event_for_each_line_item()
    {
        var order = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.NewGuid(),
            [
                new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista),
                new ItemSelection(ItemType.Croissant, PreparationStation.Kitchen)
            ]);

        var events = order.DomainEvents.Cast<OrderItemAccepted>().ToArray();

        Assert.Collection(
            events,
            drink =>
            {
                Assert.Equal(order.Id, drink.OrderId);
                Assert.Equal(order.LineItems[0].Id, drink.LineItemId);
                Assert.Equal(ItemType.Cappuccino, drink.ItemType);
                Assert.Equal(PreparationStation.Barista, drink.Station);
            },
            food =>
            {
                Assert.Equal(order.Id, food.OrderId);
                Assert.Equal(order.LineItems[1].Id, food.LineItemId);
                Assert.Equal(ItemType.Croissant, food.ItemType);
                Assert.Equal(PreparationStation.Kitchen, food.Station);
            });
    }

    [Fact]
    public void Clear_domain_events_removes_pending_events()
    {
        var order = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.NewGuid(),
            [new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista)]);

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
    }
}
