using System.Diagnostics;
using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Messaging.Dapr;

internal sealed class DaprIntegrationEventPublisher(
    IOptions<DaprMessagingOptions> options,
    IDaprPubSubClient client) : IIntegrationEventPublisher
{
    private readonly DaprMessagingOptions _options = options.Value;

    public async Task PublishAsync<TPayload>(
        string key,
        IntegrationEventEnvelope<TPayload> message,
        MessageIdentity identity,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(identity);
        ValidateIdentity(message, identity);
        var topic = IntegrationEventTopicResolver.Resolve<TPayload>(_options.TopicPrefix);
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = MessagingTelemetry.StartProducerActivity(
            "dapr",
            topic,
            message.EventType,
            message.MessageId,
            identity);
        try
        {
            var propagationIdentity =
                MessagingTelemetry.ContinueFromCurrentActivity(identity);
            await client.PublishEventAsync(
                _options.PubSubName,
                topic,
                message,
                CreateMetadata(key, message, propagationIdentity),
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

    private static Dictionary<string, string> CreateMetadata<TPayload>(
        string key,
        IntegrationEventEnvelope<TPayload> message,
        MessageIdentity identity)
        where TPayload : IIntegrationEvent
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["partitionKey"] = key,
            ["cloudevent.id"] = message.MessageId.ToString("D"),
            ["cloudevent.type"] = message.EventType,
            ["cloudevent.source"] = "coffeeshop",
            ["cloudevent.subject"] = key,
            ["cloudevent.correlationid"] = identity.CorrelationId
        };
        AddIfPresent(metadata, "cloudevent.causationid", identity.CausationId);
        AddIfPresent(metadata, "cloudevent.traceparent", identity.TraceParent);
        AddIfPresent(metadata, "cloudevent.tracestate", identity.TraceState);
        return metadata;
    }

    private static void ValidateIdentity<TPayload>(
        IntegrationEventEnvelope<TPayload> message,
        MessageIdentity identity)
        where TPayload : IIntegrationEvent
    {
        if (!string.Equals(
                message.CorrelationId,
                identity.CorrelationId,
                StringComparison.Ordinal)
            || !string.Equals(
                message.CausationId,
                identity.CausationId,
                StringComparison.Ordinal))
        {
            throw new JsonException("Dapr publication identity does not match the envelope.");
        }

        if (identity.TraceParent is null)
        {
            if (identity.TraceState is not null)
            {
                throw new JsonException("Dapr trace state requires a trace parent.");
            }

            return;
        }

        if (identity.TraceParent.Length > 128
            || identity.TraceState?.Length > 512
            || !ActivityContext.TryParse(
                identity.TraceParent,
                identity.TraceState,
                isRemote: true,
                out _))
        {
            throw new JsonException("Dapr trace context is invalid.");
        }
    }

    private static void AddIfPresent(
        IDictionary<string, string> metadata,
        string name,
        string? value)
    {
        if (value is not null)
        {
            metadata.Add(name, value);
        }
    }
}
