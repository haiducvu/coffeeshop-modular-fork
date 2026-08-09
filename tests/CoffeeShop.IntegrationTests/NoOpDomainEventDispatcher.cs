using CoffeeShop.Application.Common.Events;
using CoffeeShop.Domain.Common;

namespace CoffeeShop.IntegrationTests;

internal sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
