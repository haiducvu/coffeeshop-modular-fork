using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;

internal sealed class KitchenOutboxPublisher(
    IKitchenOutboxStore store,
    IIntegrationEventPublisher transport,
    IOptions<KitchenOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<KitchenOutboxPublisher> logger)
{
    private const string InvalidContract = "invalid-contract";
    private const string PublishFailed = "publish-failed";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    private readonly KitchenOutboxOptions _options = options.Value;

    public async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        var messages = await store.ClaimBatchAsync(
            leaseId,
            _options.BatchSize,
            now,
            now.Add(_options.LeaseDuration),
            cancellationToken);
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
                logger.LogError(
                    "Kitchen Outbox message {MessageId} was rejected with {ErrorCode}.",
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
                logger.LogWarning(
                    "Kitchen Outbox publication failed for {MessageId} with {ErrorCode}.",
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
        }

        return messages.Count;
    }

    internal static IntegrationEventEnvelope<OrderItemPreparedV1> Deserialize(
        ClaimedKitchenOutboxMessage message)
    {
        if (message.EventType != OrderItemPreparedV1.EventType
            || message.EventVersion != OrderItemPreparedV1.EventVersion)
        {
            throw new JsonException("Unsupported Kitchen Outbox contract.");
        }

        var envelope = JsonSerializer.Deserialize<
            IntegrationEventEnvelope<OrderItemPreparedV1>>(message.EnvelopeJson, JsonOptions)
            ?? throw new JsonException("Kitchen Outbox envelope cannot be null.");
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
            throw new JsonException("Kitchen Outbox metadata does not match its row.");
        }

        return envelope;
    }
}
