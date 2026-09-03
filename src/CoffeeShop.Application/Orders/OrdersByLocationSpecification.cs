using System.Linq.Expressions;
using CoffeeShop.Application.Common.Queries;
using CoffeeShop.Domain.Orders;

namespace CoffeeShop.Application.Orders;

public sealed class OrdersByLocationSpecification(Location location) : ISpecification<Order>
{
    public Expression<Func<Order, bool>> Criteria => order => order.Location == location;

    public IReadOnlyList<Expression<Func<Order, object>>> Includes { get; } = [];
}
