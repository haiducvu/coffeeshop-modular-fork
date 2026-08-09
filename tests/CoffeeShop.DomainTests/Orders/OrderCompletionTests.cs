using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders;
using CoffeeShop.Domain.Orders.Events;

namespace CoffeeShop.DomainTests.Orders;

public sealed class OrderCompletionTests
{
    [Fact]
    public void CompleteItem_keeps_order_in_progress_while_another_item_is_pending()
    {
        var order = TwoItemOrder();
        order.ClearDomainEvents();

        var changed = order.CompleteItem(
            order.LineItems[0].Id,
            "barista",
            DateTimeOffset.Parse("2026-08-09T09:00:00Z"));

        Assert.True(changed);
        Assert.Equal(ItemStatus.Fulfilled, order.LineItems[0].Status);
        Assert.Equal(ItemStatus.InProgress, order.LineItems[1].Status);
        Assert.Equal(OrderStatus.InProgress, order.Status);
    }

    [Fact]
    public void CompleteItem_fulfills_order_when_every_item_is_complete()
    {
        var order = TwoItemOrder();
        order.ClearDomainEvents();
        order.CompleteItem(order.LineItems[0].Id, "barista", DateTimeOffset.UnixEpoch);

        order.CompleteItem(order.LineItems[1].Id, "kitchen", DateTimeOffset.UnixEpoch);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        var update = Assert.IsType<OrderUpdated>(order.DomainEvents.Last());
        Assert.Equal(OrderStatus.Fulfilled, update.OrderStatus);
    }

    [Fact]
    public void CompleteItem_is_idempotent_for_duplicate_completion()
    {
        var order = TwoItemOrder();
        order.ClearDomainEvents();
        var lineItemId = order.LineItems[0].Id;
        order.CompleteItem(lineItemId, "barista", DateTimeOffset.UnixEpoch);
        var eventCount = order.DomainEvents.Count;

        var changed = order.CompleteItem(lineItemId, "barista", DateTimeOffset.UnixEpoch);

        Assert.False(changed);
        Assert.Equal(eventCount, order.DomainEvents.Count);
    }

    [Fact]
    public void CompleteItem_rejects_an_unknown_line_item()
    {
        var order = TwoItemOrder();

        Assert.Throws<DomainException>(() =>
            order.CompleteItem(Guid.NewGuid(), "barista", DateTimeOffset.UnixEpoch));
    }

    private static Order TwoItemOrder() => Order.Place(
        OrderSource.Counter,
        Location.Atlanta,
        Guid.NewGuid(),
        [
            new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista),
            new ItemSelection(ItemType.Croissant, PreparationStation.Kitchen)
        ]);
}
