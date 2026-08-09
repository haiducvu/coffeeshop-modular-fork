using CoffeeShop.Domain.Common;

namespace CoffeeShop.Application.Common.Events;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken);
}
