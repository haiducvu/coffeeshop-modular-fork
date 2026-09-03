using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;

namespace CoffeeShop.Messaging.Kafka;

internal static class KafkaTopicResolver
{
    internal static string Resolve<TPayload>(string topicPrefix)
        where TPayload : IIntegrationEvent =>
        IntegrationEventTopicResolver.Resolve<TPayload>(topicPrefix);
}
