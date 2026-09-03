using CoffeeShop.IntegrationContracts;

namespace CoffeeShop.Messaging.Dapr;

internal interface IDaprPubSubClient
{
    Task PublishEventAsync<TPayload>(
        string pubSubName,
        string topicName,
        IntegrationEventEnvelope<TPayload> data,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent;
}
