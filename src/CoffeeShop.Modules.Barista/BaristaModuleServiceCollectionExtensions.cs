using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Barista.Application;
using CoffeeShop.Modules.Barista.Infrastructure.Persistence;
using CoffeeShop.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoffeeShop.Modules.Barista;

public static class BaristaModuleServiceCollectionExtensions
{
    public static IServiceCollection AddBaristaModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<BaristaDbContext>(options => ConfigureDatabase(
            options,
            connectionString,
            enableRetries: true));
        services.AddScoped<IBaristaItemRepository, EfBaristaItemRepository>();
        services.AddScoped<IDomainEventHandler<OrderItemAccepted>, HandleBaristaOrderItemAccepted>();
        return services;
    }

    public static async Task MigrateBaristaModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BaristaDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    internal static void ConfigureDatabase(
        DbContextOptionsBuilder options,
        string connectionString,
        bool enableRetries)
    {
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "barista");
            if (enableRetries)
            {
                npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
            }
        });
    }
}
