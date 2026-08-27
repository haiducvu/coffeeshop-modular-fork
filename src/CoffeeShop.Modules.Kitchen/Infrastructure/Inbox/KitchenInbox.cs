using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Kitchen.Application.Inbox;
using CoffeeShop.Modules.Kitchen.Domain;
using CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Inbox;

internal sealed class KitchenInbox(KitchenDbContext dbContext) : IKitchenInbox
{
    public async Task<InboxDecision> BeginAsync(
        string handlerName,
        Guid messageId,
        string eventType,
        int eventVersion,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken)
    {
        var duplicate = await dbContext.InboxMessages.AnyAsync(
            message => message.HandlerName == handlerName
                && message.MessageId == messageId,
            cancellationToken);
        if (duplicate)
        {
            return InboxDecision.Duplicate;
        }

        dbContext.InboxMessages.Add(new KitchenInboxMessage(
            handlerName,
            messageId,
            eventType,
            eventVersion,
            receivedAtUtc));
        return InboxDecision.New;
    }

    public async Task CompleteAsync(
        string handlerName,
        Guid messageId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken)
    {
        var inbox = dbContext.InboxMessages.Local.Single(message =>
            message.HandlerName == handlerName && message.MessageId == messageId);
        inbox.Complete(processedAtUtc);
        var items = dbContext.ChangeTracker.Entries<KitchenItem>()
            .Select(entry => entry.Entity)
            .Where(item => item.DomainEvents.Count > 0)
            .ToArray();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsInboxDuplicate(exception))
        {
            dbContext.ChangeTracker.Clear();
            return;
        }

        foreach (var item in items)
        {
            item.ClearDomainEvents();
        }
    }

    private static bool IsInboxDuplicate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "PK_inbox_messages"
        };
}
