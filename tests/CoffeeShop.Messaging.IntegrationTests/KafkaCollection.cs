namespace CoffeeShop.Messaging.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class KafkaCollection : ICollectionFixture<KafkaFixture>
{
    public const string Name = "Kafka";
}
