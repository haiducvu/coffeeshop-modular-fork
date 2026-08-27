namespace CoffeeShop.Messaging.Abstractions;

public interface IModuleInbox
{
    Task<InboxDecision> BeginAsync(
        string handlerName,
        Guid messageId,
        string eventType,
        int eventVersion,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        string handlerName,
        Guid messageId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken);
}

public enum InboxDecision
{
    New,
    Duplicate
}
