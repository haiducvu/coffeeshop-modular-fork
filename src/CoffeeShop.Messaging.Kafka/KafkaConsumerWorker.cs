using Confluent.Kafka;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka.Retry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Kafka;

internal sealed class KafkaConsumerWorker<TPayload>(
    IOptions<KafkaMessagingOptions> options,
    KafkaIntegrationEventMapper mapper,
    KafkaRetryRouter retryRouter,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaConsumerWorker<TPayload>> logger,
    string consumerRole,
    KafkaConsumerStage stage) : BackgroundService
    where TPayload : IIntegrationEvent
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var consumerGroupRole = RetryTopicResolver.ResolveConsumerGroupRole(
            consumerRole,
            stage);
        using var consumer = new ConsumerBuilder<string, byte[]>(
                KafkaClientConfigFactory.CreateConsumer(options.Value, consumerGroupRole))
            .SetLogHandler((_, message) => KafkaLogForwarder.Log(logger, message))
            .Build();
        var originalTopic = KafkaTopicResolver.Resolve<TPayload>(options.Value.TopicPrefix);
        consumer.Subscribe(RetryTopicResolver.ResolveConsumerTopic(originalTopic, stage));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumed = consumer.Consume(stoppingToken);
                    try
                    {
                        try
                        {
                            await retryRouter.DelayIfNeededAsync(
                                originalTopic,
                                consumed.Topic,
                                consumed.Message,
                                stoppingToken);
                            var envelope = mapper.FromMessage<TPayload>(consumed.Message);
                            await using var scope = scopeFactory.CreateAsyncScope();
                            var handler = scope.ServiceProvider.GetRequiredKeyedService<
                                IIntegrationEventHandler<TPayload>>(consumerRole);
                            await handler.HandleAsync(
                                envelope,
                                new IntegrationMessageContext(
                                    consumerRole,
                                    consumed.TopicPartitionOffset.ToString(),
                                    retryRouter.ResolveDeliveryAttempt(
                                        originalTopic,
                                        consumed.Topic)),
                                stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception processingException)
                        {
                            await retryRouter.RouteAsync(
                                originalTopic,
                                consumed,
                                processingException,
                                stoppingToken);
                            logger.LogWarning(
                                "Kafka consumer {ConsumerRole} forwarded failed record from {Source}; failure type {FailureType}",
                                consumerRole,
                                consumed.TopicPartitionOffset,
                                processingException.GetType().Name);
                        }

                        consumer.Commit(consumed);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception deliveryException)
                    {
                        logger.LogWarning(
                            "Kafka consumer {ConsumerRole} did not commit {Source}; delivery will be attempted again after {FailureType}",
                            consumerRole,
                            consumed.TopicPartitionOffset,
                            deliveryException.GetType().Name);
                        consumer.Seek(consumed.TopicPartitionOffset);
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
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
