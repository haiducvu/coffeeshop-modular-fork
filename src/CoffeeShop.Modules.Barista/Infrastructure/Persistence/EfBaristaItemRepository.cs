using CoffeeShop.Modules.Barista.Application;
using CoffeeShop.Modules.Barista.Domain;
using CoffeeShop.SharedKernel.Events;

namespace CoffeeShop.Modules.Barista.Infrastructure.Persistence;

internal sealed class EfBaristaItemRepository(
    BaristaDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IBaristaItemRepository
{
    public async Task AddAsync(BaristaItem item, CancellationToken cancellationToken)
    {
        await dbContext.Items.AddAsync(item, cancellationToken);
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
        foreach (var item in items)
        {
            item.ClearDomainEvents();
        }

        if (events.Length > 0)
        {
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }
    }
}
