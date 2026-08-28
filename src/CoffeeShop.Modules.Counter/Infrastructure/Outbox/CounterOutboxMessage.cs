namespace CoffeeShop.Modules.Counter.Infrastructure.Outbox;

internal sealed class CounterOutboxMessage
{
    private CounterOutboxMessage()
    {
    }

    internal CounterOutboxMessage(
        Guid messageId,
        string eventType,
        int eventVersion,
        string envelopeJson,
        DateTimeOffset occurredAtUtc,
        string correlationId,
        string? causationId,
        string? traceParent,
        string? traceState)
    {
        MessageId = messageId;
        EventType = eventType;
        EventVersion = eventVersion;
        EnvelopeJson = envelopeJson;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        CausationId = causationId;
        TraceParent = traceParent;
        TraceState = traceState;
        NextAttemptAtUtc = occurredAtUtc;
    }

    public Guid MessageId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public int EventVersion { get; private set; }
    public string EnvelopeJson { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? CausationId { get; private set; }
    public string? TraceParent { get; private set; }
    public string? TraceState { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public DateTimeOffset? RejectedAtUtc { get; private set; }
    public string? LastErrorCode { get; private set; }
}
