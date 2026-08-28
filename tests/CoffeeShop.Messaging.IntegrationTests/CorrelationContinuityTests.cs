using System.Collections.Concurrent;
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
public sealed class CorrelationContinuityTests(
    KafkaFixture kafka,
    OutboxPostgreSqlFixture postgres)
{
    private static readonly MessageIdentity RootIdentity = new(
        "27111111-1111-1111-1111-111111111111",
        null,
        "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
        "lesson27=green");

    [Fact]
    public async Task Root_identity_survives_outbox_kafka_consumers_and_notifications()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellationToken = timeout.Token;
        await postgres.ResetAsync();
        var runId = Guid.NewGuid().ToString("N");
        var topicPrefix = $"lesson27-{runId}";
        await CreateTopicsAsync(topicPrefix);
        using var host = BuildHost(topicPrefix, runId);
        await host.Services.MigrateCounterModuleAsync(cancellationToken);
        await host.Services.MigrateBaristaModuleAsync(cancellationToken);
        await host.Services.MigrateKitchenModuleAsync(cancellationToken);
        await host.StartAsync(cancellationToken);

        try
        {
            var identityAccessor = host.Services.GetRequiredService<IMessageIdentityAccessor>();
            Guid orderId;
            using (identityAccessor.Push(RootIdentity))
            {
                await using var scope = host.Services.CreateAsyncScope();
                var counter = scope.ServiceProvider.GetRequiredService<ICounterModule>();
                orderId = (await counter.PlaceOrderAsync(
                    new PlaceOrderInput(0, 0, Guid.NewGuid(), [5], [6]),
                    cancellationToken)).OrderId;
            }

            await WaitForFulfillmentAsync(host.Services, orderId, cancellationToken);
            var rows = await ReadOutboxIdentitiesAsync(cancellationToken);
            var root = Assert.Single(rows, row => row.Module == "counter");
            var children = rows.Where(row => row.Module != "counter").ToArray();

            Assert.Equal(RootIdentity.CorrelationId, root.CorrelationId);
            Assert.Null(root.CausationId);
            Assert.Equal(RootIdentity.TraceParent, root.TraceParent);
            Assert.Equal(RootIdentity.TraceState, root.TraceState);
            Assert.Equal(2, children.Length);
            Assert.All(children, child =>
            {
                Assert.Equal(RootIdentity.CorrelationId, child.CorrelationId);
                Assert.Equal(root.MessageId.ToString("D"), child.CausationId);
                Assert.Equal(RootIdentity.TraceParent, child.TraceParent);
                Assert.Equal(RootIdentity.TraceState, child.TraceState);
            });
            Assert.Equal(3, rows.Select(row => row.MessageId).Distinct().Count());

            var updates = host.Services.GetRequiredService<RecordingOrderUpdates>();
            Assert.Equal(2, updates.Observed.Count);
            Assert.All(updates.Observed, update =>
            {
                Assert.Equal(RootIdentity.CorrelationId, update.Identity.CorrelationId);
                Assert.Contains(
                    children.Select(child => child.MessageId.ToString("D")),
                    causationId => causationId == update.Identity.CausationId);
                Assert.Equal(RootIdentity.TraceParent, update.Identity.TraceParent);
                Assert.Equal(RootIdentity.TraceState, update.Identity.TraceState);
            });
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private IHost BuildHost(string topicPrefix, string runId)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IPreparationDelay, NoPreparationDelay>();
        builder.Services.AddScoped<IDomainEventDispatcher, ServiceProviderDispatcher>();
        builder.Services.AddSingleton<IFulfillmentOrdersCache, NoOpFulfillmentCache>();
        builder.Services.AddSingleton<RecordingOrderUpdates>();
        builder.Services.AddTransient<IDomainEventHandler<OrderUpdated>>(services =>
            services.GetRequiredService<RecordingOrderUpdates>());
        builder.Services.AddKafkaMessaging(options =>
        {
            options.BootstrapServers = kafka.BootstrapServers;
            options.TopicPrefix = topicPrefix;
            options.ConsumerGroupPrefix = $"lesson27-{runId}";
        });
        builder.Services.AddCounterModule(
            postgres.ConnectionString,
            configureOutbox: ConfigureCounterOutbox);
        builder.Services.AddBaristaModule(postgres.ConnectionString, ConfigureBaristaOutbox);
        builder.Services.AddKitchenModule(postgres.ConnectionString, ConfigureKitchenOutbox);
        builder.Services.AddKafkaConsumer<OrderPlacedV1>("barista");
        builder.Services.AddKafkaConsumer<OrderPlacedV1>("kitchen");
        builder.Services.AddKafkaConsumer<OrderItemPreparedV1>("counter");
        return builder.Build();
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
            if ((await counter.GetOrderAsync(orderId, cancellationToken))?.Status == "Fulfilled")
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    private async Task<IReadOnlyList<OutboxIdentity>> ReadOutboxIdentitiesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 'counter', "MessageId", "CorrelationId", "CausationId", "TraceParent", "TraceState"
            FROM counter.outbox_messages
            UNION ALL
            SELECT 'barista', "MessageId", "CorrelationId", "CausationId", "TraceParent", "TraceState"
            FROM barista.outbox_messages
            UNION ALL
            SELECT 'kitchen', "MessageId", "CorrelationId", "CausationId", "TraceParent", "TraceState"
            FROM kitchen.outbox_messages;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<OutboxIdentity>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new OutboxIdentity(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
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

    private sealed class RecordingOrderUpdates(IMessageIdentityAccessor identityAccessor)
        : IDomainEventHandler<OrderUpdated>
    {
        public ConcurrentQueue<ObservedUpdate> Observed { get; } = new();

        public Task HandleAsync(OrderUpdated domainEvent, CancellationToken cancellationToken)
        {
            Observed.Enqueue(new ObservedUpdate(domainEvent, identityAccessor.Current));
            return Task.CompletedTask;
        }
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
                var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(
                    domainEvent.GetType());
                var method = handlerType.GetMethod("HandleAsync")!;
                foreach (var handler in services.GetServices(handlerType))
                {
                    await (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
                }
            }
        }
    }

    private sealed class NoOpFulfillmentCache : IFulfillmentOrdersCache
    {
        public Task<IReadOnlyList<FulfilledOrder>?> GetAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FulfilledOrder>?>(null);

        public Task SetAsync(
            IReadOnlyList<FulfilledOrder> orders,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record OutboxIdentity(
        string Module,
        Guid MessageId,
        string CorrelationId,
        string? CausationId,
        string? TraceParent,
        string? TraceState);

    private sealed record ObservedUpdate(
        OrderUpdated Event,
        MessageIdentity Identity);
}
