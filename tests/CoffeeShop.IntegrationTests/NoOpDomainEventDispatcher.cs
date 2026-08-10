using CoffeeShop.SharedKernel.Events;

namespace CoffeeShop.IntegrationTests;

internal sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
