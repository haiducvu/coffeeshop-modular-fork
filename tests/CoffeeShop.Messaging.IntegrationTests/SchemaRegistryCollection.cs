namespace CoffeeShop.Messaging.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class SchemaRegistryCollection : ICollectionFixture<SchemaRegistryFixture>
{
    public const string Name = "SchemaRegistry";
}
