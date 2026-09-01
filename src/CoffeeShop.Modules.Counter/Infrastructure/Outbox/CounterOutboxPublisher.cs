using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Modules.Counter.Infrastructure.Outbox;

internal sealed class CounterOutboxPublisher(
    ICounterOutboxStore store,
    IIntegrationEventPublisher transport,
    IOptions<CounterOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<CounterOutboxPublisher> logger)
{
    private const string InvalidContract = "invalid-contract";
    private const string PublishFailed = "publish-failed";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false
        };

    private readonly CounterOutboxOptions _options = options.Value;

    public async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid();
        var claimedAt = timeProvider.GetUtcNow();
        var messages = await store.ClaimBatchAsync(
            leaseId,
            _options.BatchSize,
            claimedAt,
            claimedAt.Add(_options.LeaseDuration),
            cancellationToken);
        MessagingTelemetry.RecordOutboxBatch("counter", messages.Count);

        foreach (var message in messages)
        {
            try
            {
                var envelope = Deserialize(message);
                await transport.PublishAsync(
                    envelope.Payload.OrderId.ToString("D"),
                    envelope,
                    new MessageIdentity(
                        message.CorrelationId,
                        message.CausationId,
                        message.TraceParent,
                        message.TraceState),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (JsonException)
            {
                MessagingTelemetry.RecordOutboxPublish(
                    "counter",
                    OrderPlacedV1.EventType,
                    "rejected");
                logger.LogError(
                    "Counter outbox message {MessageId} was rejected with {ErrorCode}.",
                    message.MessageId,
                    InvalidContract);
                await store.MarkRejectedAsync(
                    message.MessageId,
                    leaseId,
                    InvalidContract,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                continue;
            }
            catch
            {
                MessagingTelemetry.RecordOutboxPublish(
                    "counter",
                    OrderPlacedV1.EventType,
                    "failure");
                logger.LogWarning(
                    "Counter outbox publication failed for {MessageId} with {ErrorCode}.",
                    message.MessageId,
                    PublishFailed);
                await store.MarkFailedAsync(
                    message.MessageId,
                    leaseId,
                    PublishFailed,
                    timeProvider.GetUtcNow().Add(_options.RetryDelay),
                    cancellationToken);
                continue;
            }

            await store.MarkPublishedAsync(
                message.MessageId,
                leaseId,
                timeProvider.GetUtcNow(),
                cancellationToken);
            MessagingTelemetry.RecordOutboxPublish(
                "counter",
                OrderPlacedV1.EventType,
                "success");
        }

        return messages.Count;
    }

    internal static IntegrationEventEnvelope<OrderPlacedV1> Deserialize(
        ClaimedOutboxMessage message)
    {
        if (!string.Equals(
                message.EventType,
                OrderPlacedV1.EventType,
                StringComparison.Ordinal)
            || message.EventVersion != OrderPlacedV1.EventVersion)
        {
            throw new JsonException("Unsupported counter outbox event contract.");
        }

        var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<OrderPlacedV1>>(
            message.EnvelopeJson,
            JsonOptions)
            ?? throw new JsonException("The counter outbox envelope cannot be null.");
        if (envelope.MessageId != message.MessageId
            || !string.Equals(envelope.EventType, message.EventType, StringComparison.Ordinal)
            || envelope.EventVersion != message.EventVersion
            || !string.Equals(
                envelope.CorrelationId,
                message.CorrelationId,
                StringComparison.Ordinal)
            || !string.Equals(
                envelope.CausationId,
                message.CausationId,
                StringComparison.Ordinal))
        {
            throw new JsonException("The counter outbox envelope metadata does not match its row.");
        }

        return envelope;
    }
}
