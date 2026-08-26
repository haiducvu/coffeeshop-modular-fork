using CoffeeShop.IntegrationContracts;

namespace CoffeeShop.Messaging.Abstractions;

public interface IIntegrationEventHandler<TPayload>
    where TPayload : IIntegrationEvent
{
    Task HandleAsync(
        IntegrationEventEnvelope<TPayload> message,
        IntegrationMessageContext context,
        CancellationToken cancellationToken);
}
