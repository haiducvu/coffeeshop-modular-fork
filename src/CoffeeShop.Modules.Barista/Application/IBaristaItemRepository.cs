using CoffeeShop.Modules.Barista.Domain;

namespace CoffeeShop.Modules.Barista.Application;

internal interface IBaristaItemRepository
{
    Task AddAsync(BaristaItem item, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
