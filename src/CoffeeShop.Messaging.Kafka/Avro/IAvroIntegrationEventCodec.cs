using CoffeeShop.IntegrationContracts;

namespace CoffeeShop.Messaging.Kafka.Avro;

internal interface IAvroIntegrationEventCodec
{
    ValueTask<byte[]> SerializeAsync<TPayload>(
        string topic,
        IntegrationEventEnvelope<TPayload> envelope,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent;

    ValueTask<IntegrationEventEnvelope<TPayload>> DeserializeAsync<TPayload>(
        string topic,
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent;
}
