using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Barista.Application;
using CoffeeShop.Modules.Barista.Application.Inbox;
using CoffeeShop.Modules.Barista.Application.Outbox;
using CoffeeShop.Modules.Barista.Infrastructure.Inbox;
using CoffeeShop.Modules.Barista.Infrastructure.Outbox;
using CoffeeShop.Modules.Barista.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoffeeShop.Modules.Barista;

public static class BaristaModuleServiceCollectionExtensions
{
    public static IServiceCollection AddBaristaModule(
        this IServiceCollection services,
        string connectionString,
        Action<BaristaOutboxOptions>? configureOutbox = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<BaristaDbContext>(options => ConfigureDatabase(
            options,
            connectionString,
            enableRetries: true));
        services.AddScoped<IBaristaItemRepository, EfBaristaItemRepository>();
        services.AddScoped<IBaristaInbox, BaristaInbox>();
        services.AddScoped<IBaristaOutboxWriter, BaristaOutboxWriter>();
        services.AddKeyedScoped<
            IIntegrationEventHandler<OrderPlacedV1>,
            HandleOrderPlacedIntegrationEvent>("barista");
        if (configureOutbox is not null)
        {
            services.AddOptions<BaristaOutboxOptions>()
                .Configure(configureOutbox)
                .Validate(
                    options => options.BatchSize is >= 1 and <= 500,
                    "Barista outbox BatchSize must be between 1 and 500.")
                .Validate(
                    options => options.PollInterval >= TimeSpan.FromMilliseconds(10)
                        && options.PollInterval <= TimeSpan.FromMinutes(1),
                    "Barista outbox PollInterval must be between 10 ms and 1 minute.")
                .Validate(
                    options => options.LeaseDuration >= TimeSpan.FromSeconds(1)
                        && options.LeaseDuration <= TimeSpan.FromMinutes(10),
                    "Barista outbox LeaseDuration must be between 1 second and 10 minutes.")
                .Validate(
                    options => options.RetryDelay >= TimeSpan.FromMilliseconds(100)
                        && options.RetryDelay <= TimeSpan.FromMinutes(10),
                    "Barista outbox RetryDelay must be between 100 ms and 10 minutes.")
                .ValidateOnStart();
            services.AddScoped<IBaristaOutboxStore, BaristaOutboxStore>();
            services.AddScoped<BaristaOutboxPublisher>();
            services.AddHostedService<BaristaOutboxWorker>();
        }
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
