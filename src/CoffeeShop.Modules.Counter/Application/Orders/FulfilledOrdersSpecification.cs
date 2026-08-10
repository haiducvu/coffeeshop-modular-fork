using System.Linq.Expressions;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Counter.Application.Common;
using CoffeeShop.Modules.Counter.Domain.Orders;

namespace CoffeeShop.Modules.Counter.Application.Orders;

internal sealed class FulfilledOrdersSpecification : ISpecification<Order>
{
    public Expression<Func<Order, bool>> Criteria =>
        order => order.Status == OrderStatus.Fulfilled;

    public IReadOnlyList<Expression<Func<Order, object>>> Includes { get; } =
        [order => order.LineItems];
}
