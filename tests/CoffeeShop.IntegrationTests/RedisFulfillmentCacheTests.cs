using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace CoffeeShop.IntegrationTests;

public sealed class RedisFulfillmentCacheTests : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:8-alpine").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Redis_cache_sets_gets_removes_and_expires_fulfillment_read_models()
    {
        var options = FulfillmentCacheOptions.Create(TimeSpan.FromSeconds(5));
        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(redis => redis.Configuration = _container.GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        var cache = new RedisFulfillmentOrdersCache(
            provider.GetRequiredService<IDistributedCache>(),
            options,
            NullLogger<RedisFulfillmentOrdersCache>.Instance);
        IReadOnlyList<FulfilledOrder> expected =
        [
            new FulfilledOrder(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Fulfilled",
                [new FulfilledOrderLineItem(
                    Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    "CAPPUCCINO",
                    5.25m,
                    "Barista",
                    "Fulfilled")])
        ];

        await cache.SetAsync(expected, CancellationToken.None);

        var actual = await cache.GetAsync(CancellationToken.None);
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync("fulfilled-orders:v1");
        await cache.RemoveAsync(CancellationToken.None);
        var removed = await cache.GetAsync(CancellationToken.None);

        var order = Assert.Single(actual!);
        Assert.Equal(expected[0].Id, order.Id);
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        Assert.Null(removed);
    }
}
