using CoffeeShop.Domain.Kitchen;

namespace CoffeeShop.Application.Kitchen;

public interface IKitchenItemRepository
{
    Task AddAsync(KitchenItem item, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
