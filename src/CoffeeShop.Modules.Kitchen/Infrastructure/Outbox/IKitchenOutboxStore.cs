namespace CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;

internal interface IKitchenOutboxStore
{
    Task<IReadOnlyList<ClaimedKitchenOutboxMessage>> ClaimBatchAsync(
        Guid leaseId,
        int batchSize,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);

    Task MarkPublishedAsync(
        Guid messageId,
        Guid leaseId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid messageId,
        Guid leaseId,
        string safeErrorCode,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken);
}

internal sealed record ClaimedKitchenOutboxMessage(
    Guid MessageId,
    string EventType,
    int EventVersion,
    string EnvelopeJson);
