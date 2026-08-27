namespace CoffeeShop.Modules.Counter.Infrastructure.Outbox;

internal interface ICounterOutboxStore
{
    Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimBatchAsync(
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

internal sealed record ClaimedOutboxMessage(
    Guid MessageId,
    string EventType,
    int EventVersion,
    string EnvelopeJson,
    string? TraceParent,
    string? TraceState);
