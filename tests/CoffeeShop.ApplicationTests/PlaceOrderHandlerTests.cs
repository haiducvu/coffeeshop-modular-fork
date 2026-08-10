using CoffeeShop.Contracts.Menu;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;

namespace CoffeeShop.ApplicationTests;

public sealed class PlaceOrderHandlerTests
{
    [Fact]
    public async Task Handle_creates_and_persists_the_order()
    {
        var repository = new RecordingOrderRepository();
        var handler = new PlaceOrderHandler(repository);
        var loyaltyMemberId = Guid.NewGuid();
        var input = new PlaceOrderInput(
            0,
            0,
            loyaltyMemberId,
            [0],
            [7]);

        var result = await handler.HandleAsync(input, CancellationToken.None);

        var order = Assert.Single(repository.Orders);
        Assert.Equal(order.Id, result.OrderId);
        Assert.Equal(loyaltyMemberId, order.LoyaltyMemberId);
        Assert.Equal(1, repository.SaveChangesCallCount);
        Assert.Collection(
            order.LineItems,
            drink => Assert.Equal(ItemType.Cappuccino, drink.ItemType),
            food => Assert.Equal(ItemType.Croissant, food.ItemType));
    }
}
