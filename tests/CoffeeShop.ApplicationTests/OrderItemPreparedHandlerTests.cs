using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Counter.Application.Orders;
using CoffeeShop.Modules.Counter.Domain.Orders;

namespace CoffeeShop.ApplicationTests;

public sealed class OrderItemPreparedHandlerTests
{
    [Fact]
    public async Task Completes_the_line_item_and_saves_once()
    {
        var repository = new RecordingOrderRepository();
        var order = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.NewGuid(),
            [new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista)]);
        repository.Orders.Add(order);
        var handler = new HandleOrderItemPrepared(repository);
        var prepared = new OrderItemPrepared(
            order.Id,
            order.LineItems[0].Id,
            ItemType.Cappuccino,
            PreparationStation.Barista,
            "barista",
            DateTimeOffset.UnixEpoch);

        await handler.HandleAsync(prepared, CancellationToken.None);
        await handler.HandleAsync(prepared, CancellationToken.None);

        Assert.Equal(ItemStatus.Fulfilled, order.LineItems[0].Status);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }
}
