using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;
using CoffeeShop.Modules.Counter.Infrastructure.Outbox;
using CoffeeShop.Modules.Counter.Infrastructure.Persistence;
using CoffeeShop.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.IntegrationTests;

[Collection(CounterOutboxCollection.Name)]
public sealed class CounterOutboxKafkaTests(
    KafkaFixture kafka,
    OutboxPostgreSqlFixture postgres)
{
    [Fact]
    public async Task Pending_row_is_published_and_post_ack_crash_is_republished_after_lease_expiry()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellationToken = testTimeout.Token;
        await postgres.ResetAsync();
        var runId = Guid.NewGuid().ToString("N");
        var topicPrefix = $"lesson24-{runId}";
        var topic = $"{topicPrefix}.orders.v1";
        await CreateTopicAsync(topic);
        var timeProvider = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-27T04:05:06+00:00"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton<IDomainEventDispatcher, NoOpDomainEventDispatcher>();
        services.AddKafkaMessaging(options =>
        {
            options.BootstrapServers = kafka.BootstrapServers;
            options.TopicPrefix = topicPrefix;
            options.ConsumerGroupPrefix = $"lesson24-{runId}";
        });
        services.AddCounterModule(postgres.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await provider.MigrateCounterModuleAsync(cancellationToken);
        using var consumer = BuildConsumer(runId);
        consumer.Subscribe(topic);

        var firstOrderId = await PlaceOrderAsync(provider, cancellationToken);
        await using var publisherScope = provider.CreateAsyncScope();
        var dbContext = publisherScope.ServiceProvider.GetRequiredService<CounterDbContext>();
        var store = new CounterOutboxStore(dbContext);
        var transport = provider.GetRequiredService<IIntegrationEventPublisher>();
        var publisher = new CounterOutboxPublisher(
            store,
            transport,
            Options.Create(CreateOptions()),
            timeProvider,
            publisherScope.ServiceProvider.GetRequiredService<ILogger<CounterOutboxPublisher>>());

        Assert.Equal(1, await publisher.PublishBatchAsync(cancellationToken));
        var delivered = ConsumeEnvelope(consumer, cancellationToken);
        Assert.Equal(firstOrderId, delivered.Payload.OrderId);
        dbContext.ChangeTracker.Clear();
        Assert.NotNull((await dbContext.OutboxMessages.SingleAsync(
            message => message.MessageId == delivered.MessageId,
            cancellationToken)).PublishedAtUtc);

        var crashOrderId = await PlaceOrderAsync(provider, cancellationToken);
        var crashLease = Guid.NewGuid();
        var claimed = Assert.Single(await store.ClaimBatchAsync(
            crashLease,
            1,
            timeProvider.GetUtcNow(),
            timeProvider.GetUtcNow().AddSeconds(30),
            cancellationToken));
        var crashEnvelope = Deserialize(claimed.EnvelopeJson);
        await transport.PublishAsync(
            crashEnvelope.Payload.OrderId.ToString("D"),
            crashEnvelope,
            cancellationToken);
        var firstAttempt = ConsumeEnvelope(consumer, cancellationToken);
        Assert.Equal(crashOrderId, firstAttempt.Payload.OrderId);

        timeProvider.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(1, await publisher.PublishBatchAsync(cancellationToken));
        var secondAttempt = ConsumeEnvelope(consumer, cancellationToken);

        Assert.Equal(firstAttempt.MessageId, secondAttempt.MessageId);
        dbContext.ChangeTracker.Clear();
        var republished = await dbContext.OutboxMessages.SingleAsync(
            message => message.MessageId == firstAttempt.MessageId,
            cancellationToken);
        Assert.NotNull(republished.PublishedAtUtc);
        Assert.Null(republished.LeaseId);
    }

    private async Task CreateTopicAsync(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = kafka.BootstrapServers
        }).Build();
        await admin.CreateTopicsAsync([
            new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1
            }
        ]);
    }

    private IConsumer<string, byte[]> BuildConsumer(string runId) =>
        new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"lesson24-verifier-{runId}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();

    private static async Task<Guid> PlaceOrderAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<PlaceOrderHandler>();
        var result = await handler.HandleAsync(
            new PlaceOrderInput(0, 0, Guid.NewGuid(), [0], [7]),
            cancellationToken);
        return result.OrderId;
    }

    private static IntegrationEventEnvelope<OrderPlacedV1> ConsumeEnvelope(
        IConsumer<string, byte[]> consumer,
        CancellationToken cancellationToken)
    {
        var result = consumer.Consume(cancellationToken);
        return JsonSerializer.Deserialize<IntegrationEventEnvelope<OrderPlacedV1>>(
            result.Message.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new JsonException("Kafka message cannot be null.");
    }

    private static IntegrationEventEnvelope<OrderPlacedV1> Deserialize(string json) =>
        JsonSerializer.Deserialize<IntegrationEventEnvelope<OrderPlacedV1>>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new JsonException("Outbox envelope cannot be null.");

    private static CounterOutboxOptions CreateOptions() => new()
    {
        BatchSize = 10,
        PollInterval = TimeSpan.FromMilliseconds(50),
        LeaseDuration = TimeSpan.FromSeconds(30),
        RetryDelay = TimeSpan.FromSeconds(5)
    };

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(
            IReadOnlyCollection<IDomainEvent> events,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
