using Microsoft.AspNetCore.Authorization;

namespace CoffeeShop.Api.Authorization;

public static class CoffeeShopPolicies
{
    public const string Customer = "CoffeeShop.Customer";
    public const string FulfillmentReader = "CoffeeShop.FulfillmentReader";
    public const string Operator = "CoffeeShop.Operator";
    public const string OrderOwner = "CoffeeShop.OrderOwner";

    public const string CustomerRole = "customer";
    public const string FulfillmentReaderRole = "fulfillment-reader";
    public const string OperatorRole = "operator";

    public static IServiceCollection AddCoffeeShopAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Customer, policy => policy.RequireRole(CustomerRole));
            options.AddPolicy(FulfillmentReader, policy => policy.RequireRole(
                FulfillmentReaderRole,
                OperatorRole));
            options.AddPolicy(Operator, policy => policy.RequireRole(OperatorRole));
            options.AddPolicy(OrderOwner, policy => policy.AddRequirements(
                new OrderOwnerRequirement()));
        });
        services.AddSingleton<IAuthorizationHandler, OrderOwnerAuthorizationHandler>();
        return services;
    }
}
