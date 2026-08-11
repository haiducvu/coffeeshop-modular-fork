using CoffeeShop.Modules.Counter;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.ApplicationTests;

public sealed class GetOrderModuleTests
{
    [Fact]
    public async Task Module_returns_the_details_of_a_placed_order()
    {
        var services = new ServiceCollection();
        services.AddCounterModuleForTesting();
        await using var provider = services.BuildServiceProvider();
        var module = provider.GetRequiredService<ICounterModule>();
        var placedOrder = await module.PlaceOrderAsync(
            new PlaceOrderInput(
                0,
                0,
                Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
                [0],
                [6]),
            CancellationToken.None);

        var result = await module.GetOrderAsync(placedOrder.OrderId, CancellationToken.None);

        var order = Assert.IsType<OrderDetails>(result);
        Assert.Equal(placedOrder.OrderId, order.OrderId);
        Assert.Equal("InProgress", order.Status);
    }
}
