using CoffeeShop.SharedKernel.Events;

namespace CoffeeShop.Modules.Counter.Infrastructure.Persistence;

internal sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
