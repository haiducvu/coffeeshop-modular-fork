namespace CoffeeShop.Messaging.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class CounterOutboxCollection
    : ICollectionFixture<KafkaFixture>,
      ICollectionFixture<OutboxPostgreSqlFixture>
{
    public const string Name = "CounterOutbox";
}
