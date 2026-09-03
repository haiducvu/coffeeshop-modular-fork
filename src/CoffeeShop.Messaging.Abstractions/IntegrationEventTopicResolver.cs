using CoffeeShop.IntegrationContracts;

namespace CoffeeShop.Messaging.Abstractions;

public static class IntegrationEventTopicResolver
{
    public static string Resolve<TPayload>(string topicPrefix)
        where TPayload : IIntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicPrefix);
        return (TPayload.EventType, TPayload.EventVersion) switch
        {
            ("coffeeshop.order-placed", 1) => $"{topicPrefix}.orders.v1",
            ("coffeeshop.order-item-prepared", 1) => $"{topicPrefix}.preparation.v1",
            _ => throw new NotSupportedException(
                $"No integration topic is registered for {TPayload.EventType} version {TPayload.EventVersion}.")
        };
    }
}
