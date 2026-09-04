using Confluent.Kafka;
using Confluent.Kafka.Admin;
using CoffeeShop.Barista.Worker;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Application.Fulfillment;
using CoffeeShop.Modules.Counter.Infrastructure.Outbox;
using CoffeeShop.Modules.Kitchen;
using CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;
using CoffeeShop.SharedKernel.Events;
using CoffeeShop.SharedKernel.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CoffeeShop.Messaging.IntegrationTests;

[Collection(CounterOutboxCollection.Name)]
public sealed class ExtractedBaristaWorkflowTests(
    KafkaFixture kafka,
    OutboxPostgreSqlFixture postgres)
{
    [Fact]
    public async Task Independent_barista_host_completes_its_slice_of_a_mixed_order()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellationToken = timeout.Token;
        await postgres.ResetAsync();
        var runId = Guid.NewGuid().ToString("N");
        var topicPrefix = $"lesson31-{runId}";
        var consumerGroupPrefix = $"lesson31-{runId}";
        await CreateTopicsAsync(topicPrefix);

        var applicationBuilder = Host.CreateApplicationBuilder();
        applicationBuilder.Services.AddSingleton<IPreparationDelay, NoPreparationDelay>();
        applicationBuilder.Services.AddScoped<IDomainEventDispatcher, ServiceProviderDispatcher>();
        applicationBuilder.Services.AddSingleton<IFulfillmentOrdersCache, RecordingFulfillmentCache>();
        applicationBuilder.Services.AddKafkaMessaging(options =>
            ConfigureKafka(options, topicPrefix, consumerGroupPrefix));
        applicationBuilder.Services.AddCounterModule(
            postgres.ConnectionString,
            configureOutbox: ConfigureCounterOutbox);
        applicationBuilder.Services.AddKitchenModule(
            postgres.ConnectionString,
            ConfigureKitchenOutbox);
        applicationBuilder.Services.AddKafkaConsumer<OrderPlacedV1>("kitchen");
        applicationBuilder.Services.AddKafkaConsumer<OrderItemPreparedV1>("counter");

        var baristaBuilder = Host.CreateApplicationBuilder();
        baristaBuilder.Services.AddSingleton<IPreparationDelay, NoPreparationDelay>();
        baristaBuilder.Configuration.AddInMemoryCollection(
            BaristaConfiguration(topicPrefix, consumerGroupPrefix));
        baristaBuilder.Services.AddBaristaWorker(baristaBuilder.Configuration);

        using var applicationHost = applicationBuilder.Build();
        using var baristaHost = baristaBuilder.Build();
        await applicationHost.Services.MigrateCounterModuleAsync(cancellationToken);
        await applicationHost.Services.MigrateKitchenModuleAsync(cancellationToken);
        await baristaHost.Services.MigrateBaristaModuleAsync(cancellationToken);
        var baristaStarted = false;
        var applicationStarted = false;

        try
        {
            await baristaHost.StartAsync(cancellationToken);
            baristaStarted = true;
            await applicationHost.StartAsync(cancellationToken);
            applicationStarted = true;

            Guid orderId;
            var identityAccessor = applicationHost.Services
                .GetRequiredService<IMessageIdentityAccessor>();
            using (identityAccessor.Push(new MessageIdentity(
                       Guid.NewGuid().ToString("D"),
                       null,
                       null,
                       null)))
            {
                await using var scope = applicationHost.Services.CreateAsyncScope();
                var counter = scope.ServiceProvider.GetRequiredService<ICounterModule>();
                orderId = (await counter.PlaceOrderAsync(
                    new PlaceOrderInput(0, 0, Guid.NewGuid(), [5], [6]),
                    cancellationToken)).OrderId;
            }

            try
            {
                await WaitForFulfillmentAsync(
                    applicationHost.Services,
                    orderId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                var counts = await ReadCountsAsync(CancellationToken.None);
                throw new Xunit.Sdk.XunitException(
                    $"Split-host workflow timed out. Effects: [{string.Join(", ", counts)}].");
            }

            Assert.Equal(
                new long[] { 1, 1, 1, 1, 2 },
                await ReadCountsAsync(cancellationToken));
        }
        finally
        {
            if (baristaStarted)
            {
                await baristaHost.StopAsync(CancellationToken.None);
            }

            if (applicationStarted)
            {
                await applicationHost.StopAsync(CancellationToken.None);
            }
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

    private IReadOnlyDictionary<string, string?> BaristaConfiguration(
        string topicPrefix,
        string consumerGroupPrefix) => new Dictionary<string, string?>
        {
            ["ConnectionStrings:Barista"] = postgres.ConnectionString,
            ["Messaging:Kafka:BootstrapServers"] = kafka.BootstrapServers,
            ["Messaging:Kafka:SchemaRegistryUrl"] = "http://localhost:8081",
            ["Messaging:Kafka:ProducerFormat"] = "Json",
            ["Messaging:Kafka:TopicPrefix"] = topicPrefix,
            ["Messaging:Kafka:ConsumerGroupPrefix"] = consumerGroupPrefix,
            ["Messaging:BaristaOutbox:BatchSize"] = "10",
            ["Messaging:BaristaOutbox:PollInterval"] = "00:00:00.050",
            ["Messaging:BaristaOutbox:LeaseDuration"] = "00:00:10",
            ["Messaging:BaristaOutbox:RetryDelay"] = "00:00:00.100"
        };

    private void ConfigureKafka(
        KafkaMessagingOptions options,
        string topicPrefix,
        string consumerGroupPrefix)
    {
        options.BootstrapServers = kafka.BootstrapServers;
        options.ProducerFormat = KafkaProducerFormat.Json;
        options.TopicPrefix = topicPrefix;
        options.ConsumerGroupPrefix = consumerGroupPrefix;
    }

    private static async Task WaitForFulfillmentAsync(
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
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    private async Task<long[]> ReadCountsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM barista.items),
                (SELECT COUNT(*) FROM barista.inbox_messages WHERE "ProcessedAtUtc" IS NOT NULL),
                (SELECT COUNT(*) FROM barista.outbox_messages WHERE "PublishedAtUtc" IS NOT NULL),
                (SELECT COUNT(*) FROM kitchen.items),
                (SELECT COUNT(*) FROM counter.inbox_messages WHERE "ProcessedAtUtc" IS NOT NULL);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        return
        [
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4)
        ];
    }

    private static void ConfigureCounterOutbox(CounterOutboxOptions options)
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

    private sealed class RecordingFulfillmentCache : IFulfillmentOrdersCache
    {
        public Task<IReadOnlyList<FulfilledOrder>?> GetAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FulfilledOrder>?>(null);

        public Task SetAsync(
            IReadOnlyList<FulfilledOrder> orders,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
