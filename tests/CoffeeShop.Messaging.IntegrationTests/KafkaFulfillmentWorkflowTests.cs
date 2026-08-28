using Confluent.Kafka;
using Confluent.Kafka.Admin;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Barista.Infrastructure.Outbox;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Application.Fulfillment;
using CoffeeShop.Modules.Counter.Infrastructure.Outbox;
using CoffeeShop.Modules.Kitchen;
using CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;
using CoffeeShop.SharedKernel.Events;
using CoffeeShop.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CoffeeShop.Messaging.IntegrationTests;

[Collection(CounterOutboxCollection.Name)]
public sealed class KafkaFulfillmentWorkflowTests(
    KafkaFixture kafka,
    OutboxPostgreSqlFixture postgres)
{
    [Fact]
    public async Task Kafka_drives_each_station_once_and_preserves_order_updates_and_cache_invalidation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellationToken = timeout.Token;
        await postgres.ResetAsync();
        var runId = Guid.NewGuid().ToString("N");
        var topicPrefix = $"lesson25-{runId}";
        await CreateTopicsAsync(topicPrefix);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IPreparationDelay, NoPreparationDelay>();
        builder.Services.AddScoped<IDomainEventDispatcher, ServiceProviderDispatcher>();
        var cache = new RecordingFulfillmentCache();
        var updates = new RecordingOrderUpdates();
        builder.Services.AddSingleton<IFulfillmentOrdersCache>(cache);
        builder.Services.AddSingleton(updates);
        builder.Services.AddTransient<IDomainEventHandler<OrderUpdated>>(
            services => services.GetRequiredService<RecordingOrderUpdates>());
        builder.Services.AddKafkaMessaging(options =>
        {
            options.BootstrapServers = kafka.BootstrapServers;
            options.TopicPrefix = topicPrefix;
            options.ConsumerGroupPrefix = $"lesson25-{runId}";
        });
        builder.Services.AddCounterModule(
            postgres.ConnectionString,
            configureOutbox: ConfigureCounterOutbox);
        builder.Services.AddBaristaModule(
            postgres.ConnectionString,
            ConfigureBaristaOutbox);
        builder.Services.AddKitchenModule(
            postgres.ConnectionString,
            ConfigureKitchenOutbox);
        builder.Services.AddKafkaConsumer<OrderPlacedV1>("barista");
        builder.Services.AddKafkaConsumer<OrderPlacedV1>("kitchen");
        builder.Services.AddKafkaConsumer<OrderItemPreparedV1>("counter");
        using var host = builder.Build();
        await host.Services.MigrateCounterModuleAsync(cancellationToken);
        await host.Services.MigrateBaristaModuleAsync(cancellationToken);
        await host.Services.MigrateKitchenModuleAsync(cancellationToken);
        await host.StartAsync(cancellationToken);

        try
        {
            Guid orderId;
            var identityAccessor = host.Services.GetRequiredService<IMessageIdentityAccessor>();
            using (identityAccessor.Push(new MessageIdentity(
                Guid.NewGuid().ToString("D"),
                null,
                null,
                null)))
            {
                await using var scope = host.Services.CreateAsyncScope();
                var counter = scope.ServiceProvider.GetRequiredService<ICounterModule>();
                orderId = (await counter.PlaceOrderAsync(
                    new PlaceOrderInput(0, 0, Guid.NewGuid(), [5], [6]),
                    cancellationToken)).OrderId;
            }

            var details = await WaitForFulfillmentAsync(
                host.Services,
                orderId,
                cancellationToken);

            Assert.Equal("Fulfilled", details.Status);
            Assert.Equal(2, updates.Events.Count);
            Assert.Single(updates.Events, update =>
                update.OrderStatus == CoffeeShop.Contracts.Orders.OrderStatus.Fulfilled);
            Assert.Equal(1, cache.RemoveCount);
            Assert.Equal(
                new[] { 1L, 1L, 2L },
                await ReadEffectCountsAsync(cancellationToken));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private async Task CreateTopicsAsync(string prefix)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = kafka.BootstrapServers
        }).Build();
        await admin.CreateTopicsAsync([
            new TopicSpecification
            {
                Name = $"{prefix}.orders.v1",
                NumPartitions = 1,
                ReplicationFactor = 1
            },
            new TopicSpecification
            {
                Name = $"{prefix}.preparation.v1",
                NumPartitions = 1,
                ReplicationFactor = 1
            }
        ]);
    }

    private static async Task<OrderDetails> WaitForFulfillmentAsync(
        IServiceProvider services,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = services.CreateAsyncScope();
            var counter = scope.ServiceProvider.GetRequiredService<ICounterModule>();
            var order = await counter.GetOrderAsync(orderId, cancellationToken);
            if (order?.Status == "Fulfilled")
            {
                return order;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    private async Task<long[]> ReadEffectCountsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM barista.items),
                (SELECT COUNT(*) FROM kitchen.items),
                (SELECT COUNT(*) FROM counter.inbox_messages);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        return [reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)];
    }

    private static void ConfigureCounterOutbox(CounterOutboxOptions options)
    {
        options.BatchSize = 10;
        options.PollInterval = TimeSpan.FromMilliseconds(50);
        options.LeaseDuration = TimeSpan.FromSeconds(10);
        options.RetryDelay = TimeSpan.FromMilliseconds(100);
    }

    private static void ConfigureBaristaOutbox(BaristaOutboxOptions options)
    {
        options.BatchSize = 10;
        options.PollInterval = TimeSpan.FromMilliseconds(50);
        options.LeaseDuration = TimeSpan.FromSeconds(10);
        options.RetryDelay = TimeSpan.FromMilliseconds(100);
    }

    private static void ConfigureKitchenOutbox(KitchenOutboxOptions options)
    {
        options.BatchSize = 10;
        options.PollInterval = TimeSpan.FromMilliseconds(50);
        options.LeaseDuration = TimeSpan.FromSeconds(10);
        options.RetryDelay = TimeSpan.FromMilliseconds(100);
    }

    private sealed class NoPreparationDelay : IPreparationDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class ServiceProviderDispatcher(IServiceProvider services)
        : IDomainEventDispatcher
    {
        public async Task DispatchAsync(
            IReadOnlyCollection<IDomainEvent> events,
            CancellationToken cancellationToken)
        {
            foreach (var domainEvent in events)
            {
                var handlerType = typeof(IDomainEventHandler<>)
                    .MakeGenericType(domainEvent.GetType());
                var method = handlerType.GetMethod("HandleAsync")!;
                foreach (var handler in services.GetServices(handlerType))
                {
                    await (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
                }
            }
        }
    }

    private sealed class RecordingOrderUpdates : IDomainEventHandler<OrderUpdated>
    {
        public List<OrderUpdated> Events { get; } = [];

        public Task HandleAsync(OrderUpdated domainEvent, CancellationToken cancellationToken)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingFulfillmentCache : IFulfillmentOrdersCache
    {
        public int RemoveCount { get; private set; }

        public Task<IReadOnlyList<FulfilledOrder>?> GetAsync(
            CancellationToken cancellationToken) => Task.FromResult<
                IReadOnlyList<FulfilledOrder>?>(null);

        public Task SetAsync(
            IReadOnlyList<FulfilledOrder> orders,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveAsync(CancellationToken cancellationToken)
        {
            RemoveCount++;
            return Task.CompletedTask;
        }
    }
}
