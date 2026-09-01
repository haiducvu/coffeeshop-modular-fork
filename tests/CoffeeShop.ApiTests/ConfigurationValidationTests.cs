using CoffeeShop.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace CoffeeShop.ApiTests;

public sealed class ConfigurationValidationTests
{
    [Fact]
    public void Missing_postgresql_connection_is_rejected_with_the_option_name()
    {
        var exception = ResolveOptions(new Dictionary<string, string?>());

        Assert.Equal(CoffeeShopHostOptions.SectionName, exception.OptionsName);
        Assert.Contains("ConnectionStrings:CoffeeShop", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_enabled_redis_connection_is_rejected_with_the_option_name()
    {
        var exception = ResolveOptions(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CoffeeShop"] =
                "Host=database;Database=coffeeshop;Username=app;Password=local",
            ["ConnectionStrings:Redis"] = "redis:not-a-port"
        });

        Assert.Equal(CoffeeShopHostOptions.SectionName, exception.OptionsName);
        Assert.Contains("ConnectionStrings:Redis", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_client_origin_is_rejected_with_the_option_name()
    {
        var exception = ResolveOptions(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CoffeeShop"] =
                "Host=database;Database=coffeeshop;Username=app;Password=local",
            ["ClientOrigin"] = "file:///tmp/client"
        });

        Assert.Equal(CoffeeShopHostOptions.SectionName, exception.OptionsName);
        Assert.Contains("ClientOrigin", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://client.example/path")]
    [InlineData("https://client.example/?query=value")]
    [InlineData("https://user@client.example")]
    [InlineData("https://client.example/")]
    public void Non_canonical_client_origin_is_rejected(string clientOrigin)
    {
        var exception = ResolveOptions(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CoffeeShop"] =
                "Host=database;Database=coffeeshop;Username=app;Password=local",
            ["ClientOrigin"] = clientOrigin
        });

        Assert.Contains("ClientOrigin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_identity_without_audience_fails_startup_with_the_option_name()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "true");
            builder.UseSetting(
                "Authentication:Authority",
                "https://identity.test/realms/coffeeshop");
            builder.UseSetting("Authentication:Audience", string.Empty);
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Authentication:Audience", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_kafka_without_bootstrap_servers_fails_startup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("Messaging:Kafka:Enabled", "true");
            builder.UseSetting("Messaging:Kafka:BootstrapServers", string.Empty);
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Kafka bootstrap servers are required", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Avro_writer_without_absolute_schema_registry_url_fails_startup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("Messaging:Kafka:Enabled", "true");
            builder.UseSetting("Messaging:Kafka:BootstrapServers", "127.0.0.1:9092");
            builder.UseSetting("Messaging:Kafka:ProducerFormat", "Avro");
            builder.UseSetting("Messaging:Kafka:SchemaRegistryUrl", "schema-registry:8081");
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "Schema Registry URL must be an absolute HTTP or HTTPS URL",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Undefined_numeric_kafka_producer_format_fails_startup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("Messaging:Kafka:Enabled", "true");
            builder.UseSetting("Messaging:Kafka:BootstrapServers", "127.0.0.1:9092");
            builder.UseSetting("Messaging:Kafka:ProducerFormat", "42");
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "Kafka producer format must be Json or Avro",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    private static OptionsValidationException ResolveOptions(
        IReadOnlyDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        return Assert.Throws<OptionsValidationException>(() =>
            services.AddCoffeeShopHostOptions(configuration, requireDatabase: true));
    }
}
