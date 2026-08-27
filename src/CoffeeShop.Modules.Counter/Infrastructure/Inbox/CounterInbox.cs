using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Counter.Application.Inbox;
using CoffeeShop.Modules.Counter.Domain.Orders;
using CoffeeShop.Modules.Counter.Infrastructure.Persistence;
using CoffeeShop.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeeShop.Modules.Counter.Infrastructure.Inbox;

internal sealed class CounterInbox(
    CounterDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : ICounterInbox
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

        dbContext.InboxMessages.Add(new CounterInboxMessage(
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
        var aggregates = dbContext.ChangeTracker.Entries<Order>()
            .Select(entry => entry.Entity)
            .Where(order => order.DomainEvents.Count > 0)
            .ToArray();
        var events = aggregates.SelectMany(order => order.DomainEvents).ToArray();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsInboxDuplicate(exception))
        {
            dbContext.ChangeTracker.Clear();
            return;
        }

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        if (events.Length > 0)
        {
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }
    }

    private static bool IsInboxDuplicate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "PK_inbox_messages"
        };
}
