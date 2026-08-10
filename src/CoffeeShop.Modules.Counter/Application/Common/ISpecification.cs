using System.Linq.Expressions;

namespace CoffeeShop.Modules.Counter.Application.Common;

internal interface ISpecification<T>
{
    Expression<Func<T, bool>> Criteria { get; }
    IReadOnlyList<Expression<Func<T, object>>> Includes { get; }
}
