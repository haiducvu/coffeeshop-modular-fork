using System.Diagnostics.Metrics;
using CoffeeShop.Modules.Counter.Application.Fulfillment;
using CoffeeShop.Modules.Counter.Application.Orders.GetFulfilled;
using CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;
using CoffeeShop.Modules.Counter.Infrastructure.Caching;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Counter.Domain.Orders;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoffeeShop.ApplicationTests;

public sealed class FulfillmentCacheTests
{
    [Fact]
    public async Task Cache_hit_returns_the_cached_read_model_without_querying_the_repository()
    {
        var repository = new RecordingOrderRepository();
        IReadOnlyList<FulfilledOrder> cachedOrders =
        [
            new FulfilledOrder(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Fulfilled",
                [
                    new FulfilledOrderLineItem(
                        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                        "CAPPUCCINO",
                        5.25m,
                        "Barista",
                        "Fulfilled")
                ])
        ];
        var cache = new FulfillmentOrdersCacheProbe(cachedOrders);
        var handler = new GetFulfilledOrdersHandler(
            repository,
            cache,
            new FulfillmentCacheMetrics());

        var result = await handler.HandleAsync(CancellationToken.None);

        var order = Assert.Single(result);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), order.Id);
        Assert.Equal("CAPPUCCINO", Assert.Single(order.LineItems).Name);
        Assert.Equal(0, repository.ListCallCount);
    }

    [Fact]
    public async Task Cache_miss_populates_the_cache_for_a_following_read()
    {
        var repository = new RecordingOrderRepository();
        var order = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            [new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista)]);
        order.CompleteItem(order.LineItems[0].Id, "barista", DateTimeOffset.UnixEpoch);
        repository.Orders.Add(order);
        var cache = new FulfillmentOrdersCacheProbe(null);
        var handler = new GetFulfilledOrdersHandler(
            repository,
            cache,
            new FulfillmentCacheMetrics());

        var firstRead = await handler.HandleAsync(CancellationToken.None);
        repository.Orders.Clear();
        var secondRead = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(order.Id, Assert.Single(firstRead).Id);
        Assert.Equal(order.Id, Assert.Single(secondRead).Id);
        Assert.Equal(1, repository.ListCallCount);
    }

    [Fact]
    public async Task Fulfilled_invalidation_cannot_be_overwritten_by_an_older_cache_miss()
    {
        var repository = CreateRepositoryWithFulfilledOrder();
        var cache = new BlockingFulfillmentOrdersCache();
        var metrics = new FulfillmentCacheMetrics();
        var gate = new FulfillmentCacheGate();
        var reader = new GetFulfilledOrdersHandler(repository, cache, metrics, gate);
        var invalidator = new InvalidateFulfillmentCache(cache, metrics, gate);

        var read = reader.HandleAsync(CancellationToken.None);
        await cache.SetStarted.Task;
        var invalidation = invalidator.HandleAsync(
            new OrderUpdated(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ItemType.Cappuccino,
                ItemStatus.Fulfilled,
                OrderStatus.Fulfilled,
                "barista",
                DateTimeOffset.UnixEpoch),
            CancellationToken.None);
        Assert.False(invalidation.IsCompleted);
        cache.AllowSet.TrySetResult();
        await Task.WhenAll(read, invalidation);

        Assert.Null(cache.Snapshot);
    }

    [Fact]
    public async Task Concurrent_cache_misses_share_one_repository_query()
    {
        var repository = CreateRepositoryWithFulfilledOrder();
        var cache = new BlockingFulfillmentOrdersCache();
        var gate = new FulfillmentCacheGate();
        var reader = new GetFulfilledOrdersHandler(
            repository,
            cache,
            new FulfillmentCacheMetrics(),
            gate);

        var firstRead = reader.HandleAsync(CancellationToken.None);
        await cache.SetStarted.Task;
        var secondRead = reader.HandleAsync(CancellationToken.None);
        Assert.False(secondRead.IsCompleted);
        cache.AllowSet.TrySetResult();

        await Task.WhenAll(firstRead, secondRead);

        Assert.Equal(1, repository.ListCallCount);
    }

    [Fact]
    public async Task Malformed_cached_payload_is_treated_as_a_cache_miss()
    {
        var repository = CreateRepositoryWithFulfilledOrder();
        var logger = new RecordingLogger<RedisFulfillmentOrdersCache>();
        var cache = new RedisFulfillmentOrdersCache(
            new FixedDistributedCache([0x7B, 0x6E, 0x6F, 0x74, 0x2D, 0x6A, 0x73, 0x6F, 0x6E]),
            FulfillmentCacheOptions.Create(TimeSpan.FromSeconds(30)),
            logger);
        var handler = new GetFulfilledOrdersHandler(
            repository,
            cache,
            new FulfillmentCacheMetrics());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(repository.Orders[0].Id, Assert.Single(result).Id);
        Assert.Equal(1, repository.ListCallCount);
    }

    [Fact]
    public async Task Valid_json_missing_required_read_model_fields_is_treated_as_a_cache_miss()
    {
        var logger = new RecordingLogger<RedisFulfillmentOrdersCache>();
        var cache = new RedisFulfillmentOrdersCache(
            new FixedDistributedCache("[{}]"u8.ToArray()),
            FulfillmentCacheOptions.Create(TimeSpan.FromSeconds(30)),
            logger);

        var result = await cache.GetAsync(CancellationToken.None);

        Assert.Null(result);
        Assert.Contains(LogLevel.Warning, logger.LogLevels);
    }

    [Fact]
    public async Task Redis_read_failure_falls_back_to_the_repository_and_logs_a_warning()
    {
        var repository = CreateRepositoryWithFulfilledOrder();
        var logger = new RecordingLogger<RedisFulfillmentOrdersCache>();
        var cache = new RedisFulfillmentOrdersCache(
            new ThrowingDistributedCache(),
            FulfillmentCacheOptions.Create(TimeSpan.FromSeconds(30)),
            logger);
        var handler = new GetFulfilledOrdersHandler(
            repository,
            cache,
            new FulfillmentCacheMetrics());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(repository.Orders[0].Id, Assert.Single(result).Id);
        Assert.Equal(1, repository.ListCallCount);
        Assert.Contains(LogLevel.Warning, logger.LogLevels);
    }

    [Fact]
    public async Task Cache_read_propagates_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cache = CreateCancellationAwareRedisCache();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.GetAsync(cancellation.Token));
    }

    [Fact]
    public async Task Cache_write_propagates_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cache = CreateCancellationAwareRedisCache();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.SetAsync([], cancellation.Token));
    }

    [Fact]
    public async Task Cache_remove_propagates_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cache = CreateCancellationAwareRedisCache();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.RemoveAsync(cancellation.Token));
    }

    [Fact]
    public async Task Caller_cancelled_invalidation_does_not_record_a_metric()
    {
        var cache = CreateCancellationAwareRedisCache();
        var metrics = new FulfillmentCacheMetrics();
        var invalidations = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "coffeeshop.fulfillment.cache.invalidation")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            invalidations++);
        listener.Start();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invalidator = new InvalidateFulfillmentCache(cache, metrics);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invalidator.HandleAsync(
            new OrderUpdated(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ItemType.Cappuccino,
                ItemStatus.Fulfilled,
                OrderStatus.Fulfilled,
                "barista",
                DateTimeOffset.UnixEpoch),
            cancellation.Token));

        Assert.Equal(0, invalidations);
    }

    [Fact]
    public async Task Fulfilled_order_update_invalidates_the_cached_read_model()
    {
        var cache = new FulfillmentOrdersCacheProbe(
        [
            new FulfilledOrder(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Fulfilled",
                [])
        ]);
        var handler = new InvalidateFulfillmentCache(cache, new FulfillmentCacheMetrics());
        var update = new OrderUpdated(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ItemType.Cappuccino,
            ItemStatus.Fulfilled,
            OrderStatus.Fulfilled,
            "barista",
            DateTimeOffset.UnixEpoch);

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Null(await cache.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Non_fulfilled_order_update_does_not_invalidate_the_cached_read_model()
    {
        var cache = new FulfillmentOrdersCacheProbe(
        [
            new FulfilledOrder(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Fulfilled",
                [])
        ]);
        var handler = new InvalidateFulfillmentCache(cache, new FulfillmentCacheMetrics());
        var update = new OrderUpdated(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ItemType.Cappuccino,
            ItemStatus.InProgress,
            OrderStatus.InProgress,
            "barista",
            DateTimeOffset.UnixEpoch);

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.NotNull(await cache.GetAsync(CancellationToken.None));
    }

    [Fact]
    public void Ttl_accepts_the_documented_inclusive_boundaries()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), FulfillmentCacheOptions.Create(TimeSpan.FromSeconds(5)).TimeToLive);
        Assert.Equal(TimeSpan.FromHours(1), FulfillmentCacheOptions.Create(TimeSpan.FromHours(1)).TimeToLive);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(3601)]
    public void Ttl_rejects_values_outside_the_documented_boundaries(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FulfillmentCacheOptions.Create(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public async Task Place_order_does_not_touch_the_fulfillment_cache()
    {
        var cache = new FulfillmentOrdersCacheProbe(
        [
            new FulfilledOrder(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Fulfilled",
                [])
        ]);
        var services = new ServiceCollection();
        services.AddCounterModuleForTesting();
        services.AddSingleton<IFulfillmentOrdersCache>(cache);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ICounterModule>().PlaceOrderAsync(
            new PlaceOrderInput(
                (int)OrderSource.Counter,
                (int)Location.Atlanta,
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                [(int)ItemType.Cappuccino],
                []),
            CancellationToken.None);

        Assert.NotNull(cache.Snapshot);
        Assert.Equal(0, cache.GetCallCount);
        Assert.Equal(0, cache.SetCallCount);
        Assert.Equal(0, cache.RemoveCallCount);
    }

    [Fact]
    public async Task Cache_metrics_do_not_attach_customer_or_order_labels()
    {
        var repository = new RecordingOrderRepository();
        var cache = new FulfillmentOrdersCacheProbe(null);
        var metrics = new FulfillmentCacheMetrics();
        var measurements = new List<(string Name, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "CoffeeShop.Fulfillment.Cache")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            measurements.Add((instrument.Name, tags.ToArray())));
        listener.Start();
        var handler = new GetFulfilledOrdersHandler(repository, cache, metrics);
        var invalidator = new InvalidateFulfillmentCache(cache, metrics);

        await handler.HandleAsync(CancellationToken.None);
        await handler.HandleAsync(CancellationToken.None);
        await invalidator.HandleAsync(
            new OrderUpdated(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                ItemType.Cappuccino,
                ItemStatus.Fulfilled,
                OrderStatus.Fulfilled,
                "barista",
                DateTimeOffset.UnixEpoch),
            CancellationToken.None);

        Assert.Contains(measurements, measurement =>
            measurement.Name == "coffeeshop.fulfillment.cache.hit");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "coffeeshop.fulfillment.cache.miss");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "coffeeshop.fulfillment.cache.invalidation");
        Assert.All(measurements, measurement => Assert.Empty(measurement.Tags));
    }

    [Fact]
    public async Task Testing_module_reads_fulfillment_without_a_redis_configuration()
    {
        var services = new ServiceCollection();
        services.AddCounterModuleForTesting();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ICounterModule>()
            .GetFulfilledOrdersAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    private static RecordingOrderRepository CreateRepositoryWithFulfilledOrder()
    {
        var repository = new RecordingOrderRepository();
        var order = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            [new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista)]);
        order.CompleteItem(order.LineItems[0].Id, "barista", DateTimeOffset.UnixEpoch);
        repository.Orders.Add(order);
        return repository;
    }

    private static RedisFulfillmentOrdersCache CreateCancellationAwareRedisCache() => new(
        new CancellationAwareDistributedCache(),
        FulfillmentCacheOptions.Create(TimeSpan.FromSeconds(30)),
        new RecordingLogger<RedisFulfillmentOrdersCache>());

    private sealed class FulfillmentOrdersCacheProbe(IReadOnlyList<FulfilledOrder>? cachedOrders)
        : IFulfillmentOrdersCache
    {
        private IReadOnlyList<FulfilledOrder>? _cachedOrders = cachedOrders;
        public int GetCallCount { get; private set; }
        public int SetCallCount { get; private set; }
        public int RemoveCallCount { get; private set; }
        public IReadOnlyList<FulfilledOrder>? Snapshot => _cachedOrders;

        public Task<IReadOnlyList<FulfilledOrder>?> GetAsync(CancellationToken cancellationToken)
        {
            GetCallCount++;
            return Task.FromResult(_cachedOrders);
        }

        public Task SetAsync(IReadOnlyList<FulfilledOrder> orders, CancellationToken cancellationToken)
        {
            SetCallCount++;
            _cachedOrders = orders;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(CancellationToken cancellationToken)
        {
            RemoveCallCount++;
            _cachedOrders = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedDistributedCache(byte[] value) : IDistributedCache
    {
        public byte[]? Get(string key) => value;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult<byte[]?>(value);

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key)
        {
        }

        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class BlockingFulfillmentOrdersCache : IFulfillmentOrdersCache
    {
        private IReadOnlyList<FulfilledOrder>? _orders;

        public TaskCompletionSource SetStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowSet { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<FulfilledOrder>? Snapshot => _orders;

        public Task<IReadOnlyList<FulfilledOrder>?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_orders);

        public async Task SetAsync(
            IReadOnlyList<FulfilledOrder> orders,
            CancellationToken cancellationToken)
        {
            SetStarted.TrySetResult();
            await AllowSet.Task.WaitAsync(cancellationToken);
            _orders = orders;
        }

        public Task RemoveAsync(CancellationToken cancellationToken)
        {
            _orders = null;
            return Task.CompletedTask;
        }
    }

    private sealed class CancellationAwareDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new NotSupportedException();

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromCanceled<byte[]?>(token);

        public void Refresh(string key) => throw new NotSupportedException();

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            Task.FromCanceled(token);

        public void Remove(string key) => throw new NotSupportedException();

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            Task.FromCanceled(token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new NotSupportedException();

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default) => Task.FromCanceled(token);
    }

    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Redis is unavailable.");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromException<byte[]?>(new InvalidOperationException("Redis is unavailable."));

        public void Refresh(string key) => throw new InvalidOperationException("Redis is unavailable.");

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            Task.FromException(new InvalidOperationException("Redis is unavailable."));

        public void Remove(string key) => throw new InvalidOperationException("Redis is unavailable.");

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            Task.FromException(new InvalidOperationException("Redis is unavailable."));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("Redis is unavailable.");

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default) =>
            Task.FromException(new InvalidOperationException("Redis is unavailable."));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> LogLevels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LogLevels.Add(logLevel);
        }
    }
}
