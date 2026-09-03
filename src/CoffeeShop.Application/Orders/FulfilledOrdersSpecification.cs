using System.Linq.Expressions;
using CoffeeShop.Application.Common.Queries;
using CoffeeShop.Domain;
using CoffeeShop.Domain.Orders;

namespace CoffeeShop.Application.Orders;

public sealed class FulfilledOrdersSpecification : ISpecification<Order>
{
    public Expression<Func<Order, bool>> Criteria => order => order.Status == OrderStatus.Fulfilled;

    public IReadOnlyList<Expression<Func<Order, object>>> Includes { get; } = [order => order.LineItems];
}