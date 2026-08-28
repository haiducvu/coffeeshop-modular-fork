namespace CoffeeShop.Modules.Barista.Infrastructure.Outbox;

internal interface IBaristaOutboxStore
{
    Task<IReadOnlyList<ClaimedBaristaOutboxMessage>> ClaimBatchAsync(
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

    Task MarkRejectedAsync(
        Guid messageId,
        Guid leaseId,
        string safeErrorCode,
        DateTimeOffset rejectedAtUtc,
        CancellationToken cancellationToken);
}

internal sealed record ClaimedBaristaOutboxMessage(
    Guid MessageId,
    string EventType,
    int EventVersion,
    string EnvelopeJson,
    string CorrelationId,
    string? CausationId,
    string? TraceParent,
    string? TraceState);
