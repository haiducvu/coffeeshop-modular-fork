using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Modules.Counter;
using CoffeeShop.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoffeeShop.Messaging.IntegrationTests.Retry;

[Collection(CounterOutboxCollection.Name)]
public sealed class KafkaRetryAndDeadLetterDomainTests(
    KafkaFixture kafka,
    OutboxPostgreSqlFixture postgres)
{
    [Fact]
    public async Task Real_counter_domain_rejection_goes_directly_to_dlt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await postgres.ResetAsync();
        var runId = Guid.NewGuid().ToString("N");
        var prefix = $"lesson26-domain-{runId}";
        var originalTopic = $"{prefix}.preparation.v1";
        await CreateTopicsAsync(originalTopic);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<IDomainEventDispatcher, NoOpDomainEventDispatcher>();
        builder.Services.AddCounterModule(postgres.ConnectionString);
        builder.Services.AddKafkaMessaging(options =>
        {
            options.BootstrapServers = kafka.BootstrapServers;
            options.TopicPrefix = prefix;
            options.ConsumerGroupPrefix = $"lesson26-domain-{runId}";
        });
        builder.Services.AddKafkaConsumer<OrderItemPreparedV1>("counter");
        using var host = builder.Build();
        await host.Services.MigrateCounterModuleAsync(timeout.Token);
        await host.StartAsync(timeout.Token);

        try
        {
            var messageId = Guid.NewGuid();
            var payload = new OrderItemPreparedV1(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Latte",
                "Barista",
                "Lesson 26",
                DateTimeOffset.UtcNow);
            var envelope = new IntegrationEventEnvelope<OrderItemPreparedV1>(
                messageId,
                OrderItemPreparedV1.EventType,
                OrderItemPreparedV1.EventVersion,
                DateTimeOffset.UtcNow,
                $"workflow-{messageId:N}",
                null,
                payload);
            var publisher = host.Services.GetRequiredService<IIntegrationEventPublisher>();
            await publisher.PublishAsync(
                payload.OrderId.ToString("D"),
                envelope,
                timeout.Token);

            var deadLetter = await ConsumeAsync(
                $"{originalTopic}.dlt",
                $"domain-dlt-reader-{runId}",
                timeout.Token);

            Assert.Equal("Permanent", ReadHeader(deadLetter, KafkaHeaderNames.FailureKind));
            Assert.Equal("order-not-found", ReadHeader(deadLetter, KafkaHeaderNames.FailureCode));
            Assert.Equal("1", ReadHeader(deadLetter, KafkaHeaderNames.DeliveryAttempt));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private async Task CreateTopicsAsync(string originalTopic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = kafka.BootstrapServers
        }).Build();
        await admin.CreateTopicsAsync(
            new[]
            {
                originalTopic,
                $"{originalTopic}.retry.1",
                $"{originalTopic}.retry.2",
                $"{originalTopic}.dlt"
            }.Select(topic => new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1
            }));
    }

    private async Task<ConsumeResult<string, byte[]>> ConsumeAsync(
        string topic,
        string groupId,
        CancellationToken cancellationToken)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(topic);
        return await Task.Run(() => consumer.Consume(cancellationToken), cancellationToken);
    }

    private static string ReadHeader(
        ConsumeResult<string, byte[]> record,
        string name) => Encoding.UTF8.GetString(record.Message.Headers.GetLastBytes(name));

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(
            IReadOnlyCollection<IDomainEvent> events,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
