using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.Kafka;

namespace CoffeeShop.Messaging.IntegrationTests;

public sealed class SchemaRegistryFixture : IAsyncLifetime
{
    private const int SchemaRegistryPort = 8081;
    private readonly INetwork _network;
    private readonly KafkaContainer _kafka;
    private readonly IContainer _schemaRegistry;

    public SchemaRegistryFixture()
    {
        _network = new NetworkBuilder().Build();
        _kafka = new KafkaBuilder("apache/kafka:4.1.1")
            .WithKRaft()
            .WithNetwork(_network)
            .WithListener("kafka:19092")
            .Build();
        _schemaRegistry = new ContainerBuilder("confluentinc/cp-schema-registry:8.1.0")
            .WithNetwork(_network)
            .WithNetworkAliases("schema-registry")
            .WithEnvironment("SCHEMA_REGISTRY_HOST_NAME", "schema-registry")
            .WithEnvironment(
                "SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS",
                "PLAINTEXT://kafka:19092")
            .WithEnvironment(
                "SCHEMA_REGISTRY_LISTENERS",
                $"http://0.0.0.0:{SchemaRegistryPort}")
            .WithPortBinding(SchemaRegistryPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request
                    .ForPort(SchemaRegistryPort)
                    .ForPath("/subjects")))
            .Build();
    }

    public string SchemaRegistryUrl =>
        $"http://{_schemaRegistry.Hostname}:{_schemaRegistry.GetMappedPublicPort(SchemaRegistryPort)}";

    public string BootstrapServers => _kafka.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        await _kafka.StartAsync();
        await _schemaRegistry.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _schemaRegistry.DisposeAsync();
        await _kafka.DisposeAsync();
        await _network.DisposeAsync();
    }
}
