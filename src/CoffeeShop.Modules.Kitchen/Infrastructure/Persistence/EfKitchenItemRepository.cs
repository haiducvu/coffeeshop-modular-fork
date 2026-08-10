using CoffeeShop.Modules.Kitchen.Application;
using CoffeeShop.Modules.Kitchen.Domain;
using CoffeeShop.SharedKernel.Events;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;

internal sealed class EfKitchenItemRepository(
    KitchenDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IKitchenItemRepository
{
    public async Task AddAsync(KitchenItem item, CancellationToken cancellationToken)
    {
        await dbContext.Items.AddAsync(item, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var items = dbContext.ChangeTracker
            .Entries<KitchenItem>()
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
