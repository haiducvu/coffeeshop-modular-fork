using Confluent.Kafka;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Kafka;

internal sealed class KafkaConsumerWorker<TPayload>(
    IOptions<KafkaMessagingOptions> options,
    KafkaIntegrationEventMapper mapper,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaConsumerWorker<TPayload>> logger,
    string consumerRole) : BackgroundService
    where TPayload : IIntegrationEvent
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var consumer = new ConsumerBuilder<string, byte[]>(
                KafkaClientConfigFactory.CreateConsumer(options.Value, consumerRole))
            .SetLogHandler((_, message) => KafkaLogForwarder.Log(logger, message))
            .Build();
        consumer.Subscribe(KafkaTopicResolver.Resolve<TPayload>(options.Value.TopicPrefix));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumed = consumer.Consume(stoppingToken);
                    var envelope = mapper.FromMessage<TPayload>(consumed.Message);
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredKeyedService<
                        IIntegrationEventHandler<TPayload>>(consumerRole);
                    await handler.HandleAsync(
                        envelope,
                        new IntegrationMessageContext(
                            consumerRole,
                            consumed.TopicPartitionOffset.ToString(),
                            1),
                        stoppingToken);
                    consumer.Commit(consumed);
                }
                catch (ConsumeException exception) when (!exception.Error.IsFatal)
                {
                    logger.LogWarning(
                        "Kafka consumer {ConsumerRole} will retry transient error {KafkaErrorCode}: {KafkaErrorReason}",
                        consumerRole,
                        exception.Error.Code,
                        exception.Error.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
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
