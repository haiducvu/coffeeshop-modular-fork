using CoffeeShop.Contracts.Menu;
using CoffeeShop.Modules.Counter.Domain.Orders;
using FluentValidation;

namespace CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;

internal sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderInput>
{
    public PlaceOrderValidator()
    {
        RuleFor(input => input.LoyaltyMemberId).NotEmpty();
        RuleFor(input => input.OrderSource)
            .Must(value => Enum.IsDefined((OrderSource)value));
        RuleFor(input => input.Location)
            .Must(value => Enum.IsDefined((Location)value));
        RuleFor(input => input)
            .Must(input => input.BaristaItems.Count + input.KitchenItems.Count > 0)
            .WithName("Items")
            .WithMessage("An order must contain at least one item.");
        RuleForEach(input => input.BaristaItems)
            .Must(value => Enum.IsDefined((ItemType)value));
        RuleForEach(input => input.KitchenItems)
            .Must(value => Enum.IsDefined((ItemType)value));
    }
}
