using Confluent.Kafka;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Kafka;

internal sealed class KafkaConsumerWorker<TPayload, THandler>(
    IOptions<KafkaMessagingOptions> options,
    KafkaIntegrationEventMapper mapper,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaConsumerWorker<TPayload, THandler>> logger,
    string consumerRole) : BackgroundService
    where TPayload : IIntegrationEvent
    where THandler : class, IIntegrationEventHandler<TPayload>
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var consumer = new ConsumerBuilder<string, byte[]>(
            KafkaClientConfigFactory.CreateConsumer(options.Value, consumerRole)).Build();
        consumer.Subscribe(KafkaTopicResolver.Resolve<TPayload>(options.Value.TopicPrefix));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var consumed = consumer.Consume(stoppingToken);
                var envelope = mapper.FromMessage<TPayload>(consumed.Message);
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<THandler>();
                await handler.HandleAsync(
                    envelope,
                    new IntegrationMessageContext(
                        consumerRole,
                        consumed.TopicPartitionOffset.ToString(),
                        1),
                    stoppingToken);
                consumer.Commit(consumed);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Kafka consumer {ConsumerRole} is stopping.",
                consumerRole);
        }
        finally
        {
            consumer.Close();
        }
    }
}
