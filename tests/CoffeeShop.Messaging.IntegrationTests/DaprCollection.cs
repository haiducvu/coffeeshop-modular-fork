namespace CoffeeShop.Messaging.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class DaprCollection : ICollectionFixture<OutboxPostgreSqlFixture>
{
    public const string Name = "Dapr";
}
