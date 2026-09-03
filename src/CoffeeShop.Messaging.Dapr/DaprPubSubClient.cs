using CoffeeShop.IntegrationContracts;
using Dapr.Client;

namespace CoffeeShop.Messaging.Dapr;

internal sealed class DaprPubSubClient(DaprClient client) : IDaprPubSubClient
{
    public Task PublishEventAsync<TPayload>(
        string pubSubName,
        string topicName,
        IntegrationEventEnvelope<TPayload> data,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent => client.PublishEventAsync(
            pubSubName,
            topicName,
            data,
            new Dictionary<string, string>(metadata, StringComparer.Ordinal),
            cancellationToken);
}
