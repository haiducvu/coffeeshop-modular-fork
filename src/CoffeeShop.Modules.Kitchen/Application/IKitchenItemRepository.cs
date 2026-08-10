using CoffeeShop.Modules.Kitchen.Domain;

namespace CoffeeShop.Modules.Kitchen.Application;

internal interface IKitchenItemRepository
{
    Task AddAsync(KitchenItem item, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
