using CoffeeShop.Modules.Counter;
using Microsoft.AspNetCore.Authorization;

namespace CoffeeShop.Api.Authorization;

public sealed class OrderOwnerAuthorizationHandler
    : AuthorizationHandler<OrderOwnerRequirement, OrderDetails>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrderOwnerRequirement requirement,
        OrderDetails order)
    {
        if (context.User.IsInRole(CoffeeShopPolicies.OperatorRole))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var subject = context.User.FindFirst("sub")?.Value;
        if (context.User.IsInRole(CoffeeShopPolicies.CustomerRole)
            && Guid.TryParse(subject, out var loyaltyMemberId)
            && loyaltyMemberId == order.LoyaltyMemberId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
