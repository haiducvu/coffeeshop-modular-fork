using System.Diagnostics;
using Confluent.Kafka;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Kafka;

internal sealed class KafkaIntegrationEventPublisher : IIntegrationEventPublisher, IDisposable
{
    private readonly KafkaMessagingOptions _options;
    private readonly KafkaIntegrationEventMapper _mapper;
    private readonly IProducer<string, byte[]> _producer;

    public KafkaIntegrationEventPublisher(
        IOptions<KafkaMessagingOptions> options,
        KafkaIntegrationEventMapper mapper,
        ILogger<KafkaIntegrationEventPublisher> logger)
    {
        _options = options.Value;
        _mapper = mapper;
        _producer = new ProducerBuilder<string, byte[]>(
                KafkaClientConfigFactory.CreateProducer(_options))
            .SetLogHandler((_, message) => KafkaLogForwarder.Log(logger, message))
            .Build();
    }

    public async Task PublishAsync<TPayload>(
        string key,
        IntegrationEventEnvelope<TPayload> message,
        MessageIdentity identity,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        var topic = KafkaTopicResolver.Resolve<TPayload>(_options.TopicPrefix);
        KafkaIntegrationEventMapper.ValidateTraceContext(
            identity.TraceParent,
            identity.TraceState);
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = MessagingTelemetry.StartProducerActivity(
            topic,
            message.EventType,
            message.MessageId,
            identity);
        try
        {
            var propagationIdentity =
                MessagingTelemetry.ContinueFromCurrentActivity(identity);
            var kafkaMessage = await _mapper.ToMessageAsync(
                topic,
                key,
                message,
                propagationIdentity,
                cancellationToken);
            await _producer.ProduceAsync(
                topic,
                kafkaMessage,
                cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            MessagingTelemetry.RecordPublish(
                message.EventType,
                topic,
                "success",
                Stopwatch.GetElapsedTime(startedAt));
        }
        catch (OperationCanceledException)
        {
            MessagingTelemetry.RecordPublish(
                message.EventType,
                topic,
                "cancelled",
                Stopwatch.GetElapsedTime(startedAt));
            throw;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error, "publish-failed");
            MessagingTelemetry.RecordPublish(
                message.EventType,
                topic,
                "failure",
                Stopwatch.GetElapsedTime(startedAt));
            throw;
        }
    }

    public void Dispose() => _producer.Dispose();
}
