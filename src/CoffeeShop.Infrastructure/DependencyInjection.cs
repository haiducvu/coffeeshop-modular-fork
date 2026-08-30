using CoffeeShop.Application.Orders;
using Microsoft.Extensions.DependencyInjection;
using CoffeeShop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCoffeeShopInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<CoffeeShopDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IOrderRepository, EfOrderRepository>();
        return services;
    }
}