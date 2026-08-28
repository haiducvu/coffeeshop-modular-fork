using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Kafka.Retry;

internal interface IKafkaRetryPublisher
{
    Task PublishAsync(
        string topic,
        Message<string, byte[]> message,
        CancellationToken cancellationToken);
}

internal sealed class KafkaRetryPublisher : IKafkaRetryPublisher, IDisposable
{
    private readonly IProducer<string, byte[]> _producer;

    public KafkaRetryPublisher(
        IOptions<KafkaMessagingOptions> options,
        ILogger<KafkaRetryPublisher> logger)
    {
        _producer = new ProducerBuilder<string, byte[]>(
                KafkaClientConfigFactory.CreateProducer(options.Value))
            .SetLogHandler((_, message) => KafkaLogForwarder.Log(logger, message))
            .Build();
    }

    public async Task PublishAsync(
        string topic,
        Message<string, byte[]> message,
        CancellationToken cancellationToken)
    {
        await _producer.ProduceAsync(topic, message, cancellationToken);
    }

    public void Dispose() => _producer.Dispose();
}
