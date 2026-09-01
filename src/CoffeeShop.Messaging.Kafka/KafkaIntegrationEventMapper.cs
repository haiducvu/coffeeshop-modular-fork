using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka.Avro;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Kafka;

internal sealed class KafkaIntegrationEventMapper(
    DualFormatIntegrationEventCodec codec,
    IOptions<KafkaMessagingOptions> options)
{
    internal ValueTask<Message<string, byte[]>> ToMessageAsync<TPayload>(
        string topic,
        string key,
        IntegrationEventEnvelope<TPayload> envelope,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent => ToMessageAsync(
            topic,
            key,
            envelope,
            new MessageIdentity(
                envelope.CorrelationId,
                envelope.CausationId,
                null,
                null),
            cancellationToken);

    internal async ValueTask<Message<string, byte[]>> ToMessageAsync<TPayload>(
        string topic,
        string key,
        IntegrationEventEnvelope<TPayload> envelope,
        MessageIdentity identity,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(identity);
        if (!string.Equals(
                envelope.CorrelationId,
                identity.CorrelationId,
                StringComparison.Ordinal)
            || !string.Equals(
                envelope.CausationId,
                identity.CausationId,
                StringComparison.Ordinal))
        {
            throw new JsonException("Kafka publication identity does not match the envelope.");
        }

        ValidateTraceContext(identity.TraceParent, identity.TraceState);
        var encoded = await codec.SerializeAsync(
            topic,
            envelope,
            options.Value.ProducerFormat,
            cancellationToken);

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
        Add(headers, KafkaHeaderNames.ContentType, encoded.ContentType);
        AddIfPresent(headers, KafkaHeaderNames.TraceParent, identity.TraceParent);
        AddIfPresent(headers, KafkaHeaderNames.TraceState, identity.TraceState);

        return new Message<string, byte[]>
        {
            Key = key,
            Value = encoded.Value,
            Headers = headers
        };
    }

    internal async ValueTask<IntegrationEventEnvelope<TPayload>> FromMessageAsync<TPayload>(
        string topic,
        Message<string, byte[]> message,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(message);
        var value = message.Value ?? throw new JsonException("Kafka message value cannot be null.");
        var contentType = RequireSingle(message.Headers, KafkaHeaderNames.ContentType);
        var envelope = await codec.DeserializeAsync<TPayload>(
            topic,
            value,
            contentType,
            cancellationToken);

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

    private static void AddIfPresent(Headers headers, string name, string? value)
    {
        if (value is not null)
        {
            Add(headers, name, value);
        }
    }

    internal static void ValidateTraceContext(string? traceParent, string? traceState)
    {
        if (traceParent is null)
        {
            if (traceState is not null)
            {
                throw new JsonException("Kafka trace state requires a trace parent.");
            }

            return;
        }

        if (traceParent.Length > 128
            || traceState?.Length > 512
            || !ActivityContext.TryParse(traceParent, traceState, isRemote: true, out _))
        {
            throw new JsonException("Kafka trace context is invalid.");
        }
    }

    private static void RequireEqual(Headers headers, string name, string expected)
    {
        var actual = RequireSingle(headers, name);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new JsonException($"Kafka header '{name}' does not match the envelope.");
        }
    }

    private static string RequireSingle(Headers headers, string name)
    {
        var matches = headers
            .Where(header => string.Equals(header.Key, name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new JsonException($"Kafka header '{name}' must appear exactly once.");
        }

        return Encoding.UTF8.GetString(matches[0].GetValueBytes());
    }
}
