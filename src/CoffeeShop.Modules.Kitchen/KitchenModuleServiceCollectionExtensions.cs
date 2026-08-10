using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Kitchen.Application;
using CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;
using CoffeeShop.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoffeeShop.Modules.Kitchen;

public static class KitchenModuleServiceCollectionExtensions
{
    public static IServiceCollection AddKitchenModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<KitchenDbContext>(options => ConfigureDatabase(
            options,
            connectionString,
            enableRetries: true));
        services.AddScoped<IKitchenItemRepository, EfKitchenItemRepository>();
        services.AddScoped<IDomainEventHandler<OrderItemAccepted>, HandleKitchenOrderItemAccepted>();
        return services;
    }

    public static async Task MigrateKitchenModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<KitchenDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    internal static void ConfigureDatabase(
        DbContextOptionsBuilder options,
        string connectionString,
        bool enableRetries)
    {
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "kitchen");
            if (enableRetries)
            {
                npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
            }
        });
    }
}
