using Confluent.SchemaRegistry;

namespace CoffeeShop.Messaging.IntegrationTests.Avro;

[Collection(SchemaRegistryCollection.Name)]
public sealed class SchemaCompatibilityTests(SchemaRegistryFixture fixture)
{
    private const string Subject = "CoffeeShop.Events.Compatibility.OrderPlacedV1Lesson28";

    [Fact]
    public async Task Backward_policy_accepts_defaulted_source_and_rejects_source_without_default()
    {
        using var schemaRegistry = new CachedSchemaRegistryClient(
            new SchemaRegistryConfig { Url = fixture.SchemaRegistryUrl });
        var baseline = ReadSchema("order-placed-v1-baseline.avsc");
        var compatible = ReadSchema("order-placed-v1-compatible.avsc");
        var breaking = ReadSchema("order-placed-v1-breaking.avsc");

        await schemaRegistry.UpdateCompatibilityAsync(Compatibility.Backward, Subject);
        await schemaRegistry.RegisterSchemaAsync(Subject, baseline);

        Assert.Equal(
            Compatibility.Backward,
            await schemaRegistry.GetCompatibilityAsync(Subject));
        Assert.True(await schemaRegistry.IsCompatibleAsync(Subject, compatible));
        Assert.False(
            await schemaRegistry.IsCompatibleAsync(Subject, breaking),
            "The 'source' field without a default must be rejected by BACKWARD compatibility.");
    }

    private static Schema ReadSchema(string fileName) =>
        new(
            File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "Avro",
                "Fixtures",
                fileName)),
            SchemaType.Avro);
}
