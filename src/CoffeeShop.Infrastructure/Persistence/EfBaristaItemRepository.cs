using CoffeeShop.Application.Barista;
using CoffeeShop.Application.Common.Events;
using CoffeeShop.Domain.Barista;

namespace CoffeeShop.Infrastructure.Persistence;

public sealed class EfBaristaItemRepository(
    CoffeeShopDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IBaristaItemRepository
{
    public async Task AddAsync(BaristaItem item, CancellationToken cancellationToken)
    {
        await dbContext.BaristaItems.AddAsync(item, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var items = dbContext.ChangeTracker
            .Entries<BaristaItem>()
            .Select(entry => entry.Entity)
            .Where(item => item.DomainEvents.Count > 0)
            .ToArray();
        var events = items.SelectMany(item => item.DomainEvents).ToArray();

        await dbContext.SaveChangesAsync(cancellationToken);
        if (events.Length == 0)
        {
            return;
        }

        await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        foreach (var item in items)
        {
            item.ClearDomainEvents();
        }
    }
}
