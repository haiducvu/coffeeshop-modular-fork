using System.Text.Json;
using CoffeeShop.IntegrationContracts;

namespace CoffeeShop.Messaging.Kafka.Avro;

internal sealed class DualFormatIntegrationEventCodec(
    JsonIntegrationEventCodec jsonCodec,
    IAvroIntegrationEventCodec avroCodec)
{
    internal const string AvroContentType = "application/avro";
    internal const string JsonContentType = "application/json";

    internal async ValueTask<EncodedIntegrationEvent> SerializeAsync<TPayload>(
        string topic,
        IntegrationEventEnvelope<TPayload> envelope,
        KafkaProducerFormat format,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        if (format == KafkaProducerFormat.Json)
        {
            return new EncodedIntegrationEvent(
                JsonContentType,
                jsonCodec.Serialize(envelope));
        }

        if (format == KafkaProducerFormat.Avro)
        {
            var value = await avroCodec.SerializeAsync(
                topic,
                envelope,
                cancellationToken);
            return new EncodedIntegrationEvent(AvroContentType, value);
        }

        throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown producer format.");
    }

    internal ValueTask<IntegrationEventEnvelope<TPayload>> DeserializeAsync<TPayload>(
        string topic,
        ReadOnlyMemory<byte> value,
        string contentType,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        if (contentType == JsonContentType)
        {
            return ValueTask.FromResult(jsonCodec.Deserialize<TPayload>(value.Span));
        }

        if (contentType == AvroContentType)
        {
            return avroCodec.DeserializeAsync<TPayload>(topic, value, cancellationToken);
        }

        return ValueTask.FromException<IntegrationEventEnvelope<TPayload>>(
            new JsonException($"Kafka content type '{contentType}' is not supported."));
    }
}

internal sealed record EncodedIntegrationEvent(string ContentType, byte[] Value);
