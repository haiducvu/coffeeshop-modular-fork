namespace CoffeeShop.Modules.Counter.Infrastructure.Inbox;

internal sealed class CounterInboxMessage
{
    private CounterInboxMessage()
    {
    }

    internal CounterInboxMessage(
        string handlerName,
        Guid messageId,
        string eventType,
        int eventVersion,
        DateTimeOffset receivedAtUtc)
    {
        HandlerName = handlerName;
        MessageId = messageId;
        EventType = eventType;
        EventVersion = eventVersion;
        ReceivedAtUtc = receivedAtUtc;
    }

    public string HandlerName { get; private set; } = string.Empty;
    public Guid MessageId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public int EventVersion { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public string? Result { get; private set; }

    internal void Complete(DateTimeOffset processedAtUtc)
    {
        ProcessedAtUtc = processedAtUtc;
        Result = "processed";
    }
}
