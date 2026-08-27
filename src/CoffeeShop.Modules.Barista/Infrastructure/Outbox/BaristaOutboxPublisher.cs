using System.Text.Json;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Modules.Barista.Infrastructure.Outbox;

internal sealed class BaristaOutboxPublisher(
    IBaristaOutboxStore store,
    IIntegrationEventPublisher transport,
    IOptions<BaristaOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<BaristaOutboxPublisher> logger)
{
    private const string PublishFailed = "publish-failed";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    private readonly BaristaOutboxOptions _options = options.Value;

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
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                logger.LogWarning(
                    "Barista Outbox publication failed for {MessageId} with {ErrorCode}.",
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

    private static IntegrationEventEnvelope<OrderItemPreparedV1> Deserialize(
        ClaimedBaristaOutboxMessage message)
    {
        if (message.EventType != OrderItemPreparedV1.EventType
            || message.EventVersion != OrderItemPreparedV1.EventVersion)
        {
            throw new JsonException("Unsupported Barista Outbox contract.");
        }

        var envelope = JsonSerializer.Deserialize<
            IntegrationEventEnvelope<OrderItemPreparedV1>>(message.EnvelopeJson, JsonOptions)
            ?? throw new JsonException("Barista Outbox envelope cannot be null.");
        if (envelope.MessageId != message.MessageId)
        {
            throw new JsonException("Barista Outbox metadata does not match its row.");
        }

        return envelope;
    }
}
