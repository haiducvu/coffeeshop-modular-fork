using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders;
using FluentValidation;

namespace CoffeeShop.Application.Orders.PlaceOrder;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(command => command.LoyaltyMemberId).NotEmpty();
        RuleFor(command => command.OrderSource)
            .Must(value => Enum.IsDefined((OrderSource)value));
        RuleFor(command => command.Location)
            .Must(value => Enum.IsDefined((Location)value));
        RuleFor(command => command)
            .Must(command => command.BaristaItems.Count + command.KitchenItems.Count > 0)
            .WithName("Items")
            .WithMessage("An order must contain at least one item.");

        RuleForEach(command => command.BaristaItems).ChildRules(item =>
            item.RuleFor(value => value.ItemType)
                .Must(IsDefinedItemType));
        RuleForEach(command => command.KitchenItems).ChildRules(item =>
            item.RuleFor(value => value.ItemType)
                .Must(IsDefinedItemType));
    }

    private static bool IsDefinedItemType(int value) =>
        Enum.IsDefined((ItemType)value);
}
