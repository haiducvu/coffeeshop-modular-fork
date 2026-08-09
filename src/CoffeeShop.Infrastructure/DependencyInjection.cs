using CoffeeShop.Application.Common.Events;
using CoffeeShop.Application.Orders;
using CoffeeShop.Infrastructure.Events;
using CoffeeShop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCoffeeShopInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<CoffeeShopDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
        services.AddScoped<IOrderRepository, EfOrderRepository>();
        return services;
    }
}
