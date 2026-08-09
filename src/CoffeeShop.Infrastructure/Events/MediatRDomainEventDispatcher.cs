using CoffeeShop.Application.Common.Events;
using CoffeeShop.Domain.Common;
using MediatR;

namespace CoffeeShop.Infrastructure.Events;

public sealed class MediatRDomainEventDispatcher(IPublisher publisher)
    : IDomainEventDispatcher
{
    public async Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in events)
        {
            var notificationType = typeof(DomainEventNotification<>)
                .MakeGenericType(domainEvent.GetType());
            var notification = Activator.CreateInstance(notificationType, domainEvent)
                ?? throw new InvalidOperationException(
                    $"Could not wrap domain event {domainEvent.GetType().Name}.");

            await publisher.Publish(notification, cancellationToken);
        }
    }
}
