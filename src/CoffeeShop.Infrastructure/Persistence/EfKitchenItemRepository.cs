using CoffeeShop.Application.Common.Events;
using CoffeeShop.Application.Kitchen;
using CoffeeShop.Domain.Kitchen;

namespace CoffeeShop.Infrastructure.Persistence;

public sealed class EfKitchenItemRepository(
    CoffeeShopDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IKitchenItemRepository
{
    public async Task AddAsync(KitchenItem item, CancellationToken cancellationToken)
    {
        await dbContext.KitchenItems.AddAsync(item, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var items = dbContext.ChangeTracker.Entries<KitchenItem>()
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
