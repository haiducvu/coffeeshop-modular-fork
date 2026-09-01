using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;

namespace CoffeeShop.Messaging.Kafka;

internal sealed class KafkaMessageIdentityScope(IMessageIdentityAccessor identityAccessor)
{
    internal IDisposable Push<TPayload>(
        IntegrationEventEnvelope<TPayload> envelope,
        Headers headers)
        where TPayload : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(headers);
        var (traceParent, traceState) = ReadTraceContext(headers);

        var identity = MessagingTelemetry.ContinueFromCurrentActivity(new MessageIdentity(
            envelope.CorrelationId,
            envelope.MessageId.ToString("D"),
            traceParent,
            traceState));
        return identityAccessor.Push(identity);
    }

    internal static (string? TraceParent, string? TraceState) ReadTraceContext(
        Headers headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var traceParent = ReadOptional(headers, KafkaHeaderNames.TraceParent);
        var traceState = ReadOptional(headers, KafkaHeaderNames.TraceState);
        KafkaIntegrationEventMapper.ValidateTraceContext(traceParent, traceState);
        return (traceParent, traceState);
    }

    private static string? ReadOptional(Headers headers, string name)
    {
        var matches = headers
            .Where(header => string.Equals(header.Key, name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length > 1)
        {
            throw new JsonException($"Kafka header '{name}' must not be duplicated.");
        }

        return matches.Length == 0
            ? null
            : Encoding.UTF8.GetString(matches[0].GetValueBytes());
    }
}
