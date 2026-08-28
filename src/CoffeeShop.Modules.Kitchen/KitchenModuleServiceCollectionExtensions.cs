using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Kitchen.Application;
using CoffeeShop.Modules.Kitchen.Application.Inbox;
using CoffeeShop.Modules.Kitchen.Application.Outbox;
using CoffeeShop.Modules.Kitchen.Infrastructure.Inbox;
using CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;
using CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoffeeShop.Modules.Kitchen;

public static class KitchenModuleServiceCollectionExtensions
{
    public static IServiceCollection AddKitchenModule(
        this IServiceCollection services,
        string connectionString,
        Action<KitchenOutboxOptions>? configureOutbox = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMessageIdentityAccessor, MessageIdentityAccessor>();
        services.AddDbContext<KitchenDbContext>(options => ConfigureDatabase(
            options,
            connectionString,
            enableRetries: true));
        services.AddScoped<IKitchenItemRepository, EfKitchenItemRepository>();
        services.AddScoped<IKitchenInbox, KitchenInbox>();
        services.AddScoped<IKitchenOutboxWriter, KitchenOutboxWriter>();
        services.AddKeyedScoped<
            IIntegrationEventHandler<OrderPlacedV1>,
            HandleOrderPlacedIntegrationEvent>("kitchen");
        if (configureOutbox is not null)
        {
            services.AddOptions<KitchenOutboxOptions>()
                .Configure(configureOutbox)
                .Validate(
                    options => options.BatchSize is >= 1 and <= 500,
                    "Kitchen outbox BatchSize must be between 1 and 500.")
                .Validate(
                    options => options.PollInterval >= TimeSpan.FromMilliseconds(10)
                        && options.PollInterval <= TimeSpan.FromMinutes(1),
                    "Kitchen outbox PollInterval must be between 10 ms and 1 minute.")
                .Validate(
                    options => options.LeaseDuration >= TimeSpan.FromSeconds(1)
                        && options.LeaseDuration <= TimeSpan.FromMinutes(10),
                    "Kitchen outbox LeaseDuration must be between 1 second and 10 minutes.")
                .Validate(
                    options => options.RetryDelay >= TimeSpan.FromMilliseconds(100)
                        && options.RetryDelay <= TimeSpan.FromMinutes(10),
                    "Kitchen outbox RetryDelay must be between 100 ms and 10 minutes.")
                .ValidateOnStart();
            services.AddScoped<IKitchenOutboxStore, KitchenOutboxStore>();
            services.AddScoped<KitchenOutboxPublisher>();
            services.AddHostedService<KitchenOutboxWorker>();
        }
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
