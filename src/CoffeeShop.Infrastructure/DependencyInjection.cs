using CoffeeShop.Application.Common.Events;
using CoffeeShop.Application.Barista;
using CoffeeShop.Application.Orders;
using CoffeeShop.Infrastructure.Events;
using CoffeeShop.Infrastructure.Persistence;
using CoffeeShop.Infrastructure.Time;
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
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPreparationDelay, TaskPreparationDelay>();
        services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
        services.AddScoped<IBaristaItemRepository, EfBaristaItemRepository>();
        services.AddScoped<IOrderRepository, EfOrderRepository>();
        return services;
    }
}
