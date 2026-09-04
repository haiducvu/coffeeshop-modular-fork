using CoffeeShop.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.Kitchen.Worker.Events;

internal sealed class ServiceProviderDomainEventDispatcher(IServiceProvider serviceProvider)
    : IDomainEventDispatcher
{
    public async Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handleMethod = handlerType.GetMethod(
                    nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))
                ?? throw new InvalidOperationException(
                    $"Could not find the handler method for {domainEvent.GetType().Name}.");

            foreach (var handler in serviceProvider.GetServices(handlerType))
            {
                var resolvedHandler = handler
                    ?? throw new InvalidOperationException(
                        $"A null handler was registered for {domainEvent.GetType().Name}.");
                var task = handleMethod.Invoke(
                        resolvedHandler,
                        [domainEvent, cancellationToken]) as Task
                    ?? throw new InvalidOperationException(
                        $"Handler {resolvedHandler.GetType().Name} did not return a Task.");
                await task;
            }
        }
    }
}
