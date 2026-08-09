using CoffeeShop.Application.Common.Behaviors;
using CoffeeShop.Application.Orders.PlaceOrder;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCoffeeShopApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<PlaceOrderValidator>(ServiceLifetime.Transient);
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        return services;
    }
}
