using CoffeeShop.Application.Common.Queries;
using CoffeeShop.Domain.Orders;

namespace CoffeeShop.Application.Orders;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);
    Task<Order?> FindAsync(Guid orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> ListAsync(
        ISpecification<Order> specification,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
