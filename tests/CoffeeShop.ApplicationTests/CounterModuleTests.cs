using CoffeeShop.Modules.Counter;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.ApplicationTests;

public sealed class CounterModuleTests
{
    [Fact]
    public async Task Testing_registration_places_an_order_through_the_module_interface()
    {
        var services = new ServiceCollection();
        services.AddCounterModuleForTesting();
        await using var provider = services.BuildServiceProvider();
        var module = provider.GetRequiredService<ICounterModule>();

        var result = await module.PlaceOrderAsync(
            new PlaceOrderInput(
                0,
                0,
                Guid.NewGuid(),
                [0],
                [6]),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.OrderId);
    }
}
