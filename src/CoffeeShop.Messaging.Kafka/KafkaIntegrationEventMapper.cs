using System.Globalization;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using CoffeeShop.IntegrationContracts;

namespace CoffeeShop.Messaging.Kafka;

internal sealed class KafkaIntegrationEventMapper(JsonIntegrationEventCodec codec)
{
    private const string JsonContentType = "application/json";

    internal Message<string, byte[]> ToMessage<TPayload>(
        string key,
        IntegrationEventEnvelope<TPayload> envelope)
        where TPayload : IIntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var headers = new Headers();
        Add(headers, KafkaHeaderNames.MessageId, envelope.MessageId.ToString("D"));
        Add(headers, KafkaHeaderNames.EventType, envelope.EventType);
        Add(
            headers,
            KafkaHeaderNames.EventVersion,
            envelope.EventVersion.ToString(CultureInfo.InvariantCulture));
        Add(
            headers,
            KafkaHeaderNames.OccurredAt,
            envelope.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture));
        Add(headers, KafkaHeaderNames.CorrelationId, envelope.CorrelationId);
        Add(headers, KafkaHeaderNames.CausationId, envelope.CausationId ?? string.Empty);
        Add(headers, KafkaHeaderNames.ContentType, JsonContentType);

        return new Message<string, byte[]>
        {
            Key = key,
            Value = codec.Serialize(envelope),
            Headers = headers
        };
    }

    internal IntegrationEventEnvelope<TPayload> FromMessage<TPayload>(
        Message<string, byte[]> message)
        where TPayload : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(message);
        var value = message.Value ?? throw new JsonException("Kafka message value cannot be null.");
        var envelope = codec.Deserialize<TPayload>(value);

        RequireEqual(message.Headers, KafkaHeaderNames.ContentType, JsonContentType);
        RequireEqual(
            message.Headers,
            KafkaHeaderNames.MessageId,
            envelope.MessageId.ToString("D"));
        RequireEqual(message.Headers, KafkaHeaderNames.EventType, envelope.EventType);
        RequireEqual(
            message.Headers,
            KafkaHeaderNames.EventVersion,
            envelope.EventVersion.ToString(CultureInfo.InvariantCulture));
        RequireEqual(
            message.Headers,
            KafkaHeaderNames.OccurredAt,
            envelope.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture));
        RequireEqual(
            message.Headers,
            KafkaHeaderNames.CorrelationId,
            envelope.CorrelationId);
        RequireEqual(
            message.Headers,
            KafkaHeaderNames.CausationId,
            envelope.CausationId ?? string.Empty);

        return envelope;
    }

    private static void Add(Headers headers, string name, string value) =>
        headers.Add(name, Encoding.UTF8.GetBytes(value));

    private static void RequireEqual(Headers headers, string name, string expected)
    {
        var actualHeader = headers.LastOrDefault(header =>
            string.Equals(header.Key, name, StringComparison.Ordinal));
        var actual = actualHeader is null
            ? null
            : Encoding.UTF8.GetString(actualHeader.GetValueBytes());
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new JsonException($"Kafka header '{name}' does not match the envelope.");
        }
    }
}
