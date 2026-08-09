using CoffeeShop.Domain.Barista;

namespace CoffeeShop.Application.Barista;

public interface IBaristaItemRepository
{
    Task AddAsync(BaristaItem item, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
