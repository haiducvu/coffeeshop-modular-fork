using System.Text.Json;
using CoffeeShop.IntegrationContracts;

namespace CoffeeShop.Messaging.Kafka;

internal sealed class JsonIntegrationEventCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    internal byte[] Serialize<TPayload>(IntegrationEventEnvelope<TPayload> message)
        where TPayload : IIntegrationEvent =>
        JsonSerializer.SerializeToUtf8Bytes(message, Options);

    internal IntegrationEventEnvelope<TPayload> Deserialize<TPayload>(ReadOnlySpan<byte> value)
        where TPayload : IIntegrationEvent
    {
        var message = JsonSerializer.Deserialize<IntegrationEventEnvelope<TPayload>>(value, Options)
            ?? throw new JsonException("Kafka message value cannot be null.");

        if (!string.Equals(message.EventType, TPayload.EventType, StringComparison.Ordinal))
        {
            throw new JsonException(
                $"Event type '{message.EventType}' is not supported by {typeof(TPayload).Name}.");
        }

        if (message.EventVersion != TPayload.EventVersion)
        {
            throw new JsonException(
                $"Event version '{message.EventVersion}' is not supported by {typeof(TPayload).Name}.");
        }

        if (message.Payload is null)
        {
            throw new JsonException("Kafka message payload cannot be null.");
        }

        return message;
    }
}
