using CoffeeShop.IntegrationContracts;

namespace CoffeeShop.Messaging.Abstractions;

public interface IIntegrationEventPublisher
{
    Task PublishAsync<TPayload>(
        string key,
        IntegrationEventEnvelope<TPayload> message,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent;
}
