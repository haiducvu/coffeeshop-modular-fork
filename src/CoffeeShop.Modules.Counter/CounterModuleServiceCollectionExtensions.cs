using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Counter.Application.Fulfillment;
using CoffeeShop.Modules.Counter.Application.GetOrder;
using CoffeeShop.Modules.Counter.Application.Orders;
using CoffeeShop.Modules.Counter.Application.Orders.GetFulfilled;
using CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;
using CoffeeShop.Modules.Counter.Infrastructure.Caching;
using CoffeeShop.Modules.Counter.Infrastructure.Persistence;
using CoffeeShop.SharedKernel.Events;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace CoffeeShop.Modules.Counter;

public static class CounterModuleServiceCollectionExtensions
{
    public static IServiceCollection AddCounterModule(
        this IServiceCollection services,
        string connectionString,
        string? redisConnectionString = null,
        TimeSpan? fulfillmentCacheTimeToLive = null)
    {
        AddCoreServices(services);
        services.AddDbContext<CounterDbContext>(options => ConfigureDatabase(
            options,
            connectionString,
            enableRetries: true));
        services.AddScoped<IOrderRepository, EfOrderRepository>();
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            var cacheOptions = FulfillmentCacheOptions.Create(fulfillmentCacheTimeToLive);
            var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
            redisOptions.AbortOnConnectFail = false;
            redisOptions.ConnectRetry = 0;
            redisOptions.ConnectTimeout = (int)FulfillmentCacheOptions.CommandTimeout.TotalMilliseconds;
            redisOptions.SyncTimeout = (int)FulfillmentCacheOptions.CommandTimeout.TotalMilliseconds;
            redisOptions.AsyncTimeout = (int)FulfillmentCacheOptions.CommandTimeout.TotalMilliseconds;
            services.AddStackExchangeRedisCache(options =>
                options.ConfigurationOptions = redisOptions);
            services.AddSingleton(cacheOptions);
            services.AddSingleton<FulfillmentCacheMetrics>();
            services.AddSingleton<IFulfillmentOrdersCache, RedisFulfillmentOrdersCache>();
        }

        return services;
    }

    public static IServiceCollection AddCounterModuleForTesting(
        this IServiceCollection services)
    {
        AddCoreServices(services);
        services.AddSingleton<InMemoryOrderStore>();
        services.AddScoped<IOrderRepository, InMemoryOrderRepository>();
        services.TryAddScoped<IDomainEventDispatcher, NoOpDomainEventDispatcher>();
        return services;
    }

    public static async Task MigrateCounterModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    internal static void ConfigureDatabase(
        DbContextOptionsBuilder options,
        string connectionString,
        bool enableRetries)
    {
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "counter");
            if (enableRetries)
            {
                npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
            }
        });
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<FulfillmentCacheGate>();
        services.AddScoped<IValidator<PlaceOrderInput>, PlaceOrderValidator>();
        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<GetFulfilledOrdersHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<IDomainEventHandler<OrderItemPrepared>, HandleOrderItemPrepared>();
        services.AddScoped<IDomainEventHandler<OrderUpdated>, InvalidateFulfillmentCache>();
        services.AddScoped<ICounterModule, CounterModule>();
    }
}
