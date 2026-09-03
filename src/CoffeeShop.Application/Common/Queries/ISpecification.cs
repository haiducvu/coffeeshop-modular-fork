using System.Linq.Expressions;

namespace CoffeeShop.Application.Common.Queries;

public interface ISpecification<T>
{
    Expression<Func<T, bool>> Criteria { get; }
    IReadOnlyList<Expression<Func<T, object>>> Includes { get; }
}