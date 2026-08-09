using CoffeeShop.Application.Orders.PlaceOrder;

namespace CoffeeShop.ApplicationTests;

public sealed class PlaceOrderValidatorTests
{
    [Fact]
    public async Task Rejects_an_empty_loyalty_member_id()
    {
        var validator = new PlaceOrderValidator();

        var result = await validator.ValidateAsync(ValidCommand() with
        {
            LoyaltyMemberId = Guid.Empty
        });

        Assert.Contains(result.Errors, error => error.PropertyName == "LoyaltyMemberId");
    }

    [Fact]
    public async Task Rejects_an_order_without_items()
    {
        var validator = new PlaceOrderValidator();

        var result = await validator.ValidateAsync(ValidCommand() with
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
        var command = ValidCommand() with
        {
            OrderSource = 999,
            Location = 999,
            BaristaItems = [new PlaceOrderItem(999)]
        };

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, error => error.PropertyName == "OrderSource");
        Assert.Contains(result.Errors, error => error.PropertyName == "Location");
        Assert.Contains(result.Errors, error => error.PropertyName == "BaristaItems[0].ItemType");
    }

    private static PlaceOrderCommand ValidCommand() => new(
        0,
        0,
        Guid.NewGuid(),
        [new PlaceOrderItem(0)],
        []);
}
