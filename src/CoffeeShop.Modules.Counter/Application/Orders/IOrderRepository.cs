using CoffeeShop.Modules.Counter.Application.Common;
using CoffeeShop.Modules.Counter.Domain.Orders;

namespace CoffeeShop.Modules.Counter.Application.Orders;

internal interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);
    Task<Order?> FindAsync(Guid orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> ListAsync(
        ISpecification<Order> specification,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
