using Confluent.Kafka;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Kafka;

internal sealed class KafkaIntegrationEventPublisher : IIntegrationEventPublisher, IDisposable
{
    private readonly KafkaMessagingOptions _options;
    private readonly KafkaIntegrationEventMapper _mapper;
    private readonly IProducer<string, byte[]> _producer;

    public KafkaIntegrationEventPublisher(
        IOptions<KafkaMessagingOptions> options,
        KafkaIntegrationEventMapper mapper)
    {
        _options = options.Value;
        _mapper = mapper;
        _producer = new ProducerBuilder<string, byte[]>(
            KafkaClientConfigFactory.CreateProducer(_options)).Build();
    }

    public async Task PublishAsync<TPayload>(
        string key,
        IntegrationEventEnvelope<TPayload> message,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        var topic = KafkaTopicResolver.Resolve<TPayload>(_options.TopicPrefix);
        await _producer.ProduceAsync(
            topic,
            _mapper.ToMessage(key, message),
            cancellationToken);
    }

    public void Dispose() => _producer.Dispose();
}
