using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;

namespace CoffeeShop.ApplicationTests;

public sealed class PlaceOrderValidatorTests
{
    [Fact]
    public async Task Rejects_an_empty_loyalty_member_id()
    {
        var validator = new PlaceOrderValidator();

        var result = await validator.ValidateAsync(ValidInput() with
        {
            LoyaltyMemberId = Guid.Empty
        });

        Assert.Contains(result.Errors, error => error.PropertyName == "LoyaltyMemberId");
    }

    [Fact]
    public async Task Rejects_an_order_without_items()
    {
        var validator = new PlaceOrderValidator();

        var result = await validator.ValidateAsync(ValidInput() with
        {
            BaristaItems = [],
            KitchenItems = []
        });

        Assert.Contains(result.Errors, error => error.PropertyName == "Items");
    }

    [Fact]
    public async Task Rejects_undefined_order_location_and_item_enums()
    {
        var validator = new PlaceOrderValidator();
        var input = ValidInput() with
        {
            OrderSource = 999,
            Location = 999,
            BaristaItems = [999]
        };

        var result = await validator.ValidateAsync(input);

        Assert.Contains(result.Errors, error => error.PropertyName == "OrderSource");
        Assert.Contains(result.Errors, error => error.PropertyName == "Location");
        Assert.Contains(result.Errors, error => error.PropertyName == "BaristaItems[0]");
    }

    private static PlaceOrderInput ValidInput() => new(
        0,
        0,
        Guid.NewGuid(),
        [0],
        []);
}
