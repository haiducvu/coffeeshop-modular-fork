using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;

namespace CoffeeShop.Messaging.Kafka;

internal static class KafkaTopicResolver
{
    internal static string Resolve<TPayload>(string topicPrefix)
        where TPayload : IIntegrationEvent =>
        (TPayload.EventType, TPayload.EventVersion) switch
        {
            ("coffeeshop.order-placed", 1) => $"{topicPrefix}.orders.v1",
            ("coffeeshop.order-item-prepared", 1) => $"{topicPrefix}.preparation.v1",
            _ => throw new NotSupportedException(
                $"No Kafka topic is registered for {TPayload.EventType} version {TPayload.EventVersion}.")
        };
}
