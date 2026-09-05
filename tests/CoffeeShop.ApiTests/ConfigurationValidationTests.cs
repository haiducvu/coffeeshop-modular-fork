using CoffeeShop.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace CoffeeShop.ApiTests;

public sealed class ConfigurationValidationTests
{
    [Theory]
    [InlineData(null, ModuleHostingMode.Embedded)]
    [InlineData("Embedded", ModuleHostingMode.Embedded)]
    [InlineData("external", ModuleHostingMode.External)]
    public void Barista_hosting_mode_is_resolved_explicitly(
        string? configuredValue,
        ModuleHostingMode expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Barista:Hosting"] = configuredValue
            })
            .Build();

        Assert.Equal(
            expected,
            configuration.ResolveModuleHosting("Barista"));
    }

    [Fact]
    public void Undefined_barista_hosting_mode_is_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Barista:Hosting"] = "Shadow"
            })
            .Build();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            configuration.ResolveModuleHosting("Barista"));

        Assert.Contains(
            "Modules:Barista:Hosting",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, ModuleHostingMode.Embedded)]
    [InlineData("Embedded", ModuleHostingMode.Embedded)]
    [InlineData("external", ModuleHostingMode.External)]
    public void Kitchen_hosting_mode_is_resolved_explicitly(
        string? configuredValue,
        ModuleHostingMode expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Kitchen:Hosting"] = configuredValue
            })
            .Build();

        Assert.Equal(
            expected,
            configuration.ResolveModuleHosting("Kitchen"));
    }

    [Fact]
    public void Undefined_kitchen_hosting_mode_is_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Kitchen:Hosting"] = "Shadow"
            })
            .Build();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            configuration.ResolveModuleHosting("Kitchen"));

        Assert.Contains(
            "Modules:Kitchen:Hosting",
            exception.Message,
            StringComparison.Ordinal);
    }

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
    public Task Enabled_identity_without_audience_fails_startup_with_the_option_name()
    {
        return AssertStartupFailureAsync(
            "Authentication:Audience",
            "--Authentication:Enabled=true",
            "--Authentication:Authority=https://identity.test/realms/coffeeshop",
            "--Authentication:Audience=");
    }

    [Fact]
    public Task Enabled_kafka_without_bootstrap_servers_fails_startup()
    {
        return AssertStartupFailureAsync(
            "Kafka bootstrap servers are required",
            "--Messaging:Kafka:Enabled=true",
            "--Messaging:Kafka:BootstrapServers=");
    }

    [Fact]
    public Task Avro_writer_without_absolute_schema_registry_url_fails_startup()
    {
        return AssertStartupFailureAsync(
            "Schema Registry URL must be an absolute HTTP or HTTPS URL",
            "--Messaging:Kafka:Enabled=true",
            "--Messaging:Kafka:BootstrapServers=127.0.0.1:9092",
            "--Messaging:Kafka:ProducerFormat=Avro",
            "--Messaging:Kafka:SchemaRegistryUrl=schema-registry:8081");
    }

    [Fact]
    public Task Undefined_numeric_kafka_producer_format_fails_startup()
    {
        return AssertStartupFailureAsync(
            "Kafka producer format must be Json or Avro",
            "--Messaging:Kafka:Enabled=true",
            "--Messaging:Kafka:BootstrapServers=127.0.0.1:9092",
            "--Messaging:Kafka:ProducerFormat=42");
    }

    [Fact]
    public Task Dapr_with_external_barista_fails_startup()
    {
        return AssertStartupFailureAsync(
            "Dapr requires Modules:Barista:Hosting to be Embedded",
            "--Messaging:Kafka:Enabled=true",
            "--Messaging:Adapter=Dapr",
            "--Modules:Barista:Hosting=External");
    }

    [Fact]
    public Task Dapr_with_external_kitchen_fails_startup()
    {
        return AssertStartupFailureAsync(
            "Dapr requires Modules:Kitchen:Hosting to be Embedded",
            "--Messaging:Kafka:Enabled=true",
            "--Messaging:Adapter=Dapr",
            "--Modules:Barista:Hosting=Embedded",
            "--Modules:Kitchen:Hosting=External");
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

    private static async Task AssertStartupFailureAsync(string expectedError, params string[] settings)
    {
        // A failed RunAsync disposes its host. WebApplicationFactory's deferred
        // StartAsync can then observe a disposed IServiceProvider instead of the
        // original startup failure. Probe the real entry point in its own process.
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        startInfo.ArgumentList.Add("--environment=Testing");
        startInfo.ArgumentList.Add("--Authentication:Enabled=false");
        foreach (var setting in settings)
        {
            startInfo.ArgumentList.Add(setting);
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(output, error).WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cleanup.Token);
            throw new TimeoutException("Invalid API configuration did not terminate within the startup deadline.");
        }

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains(expectedError, await error, StringComparison.Ordinal);
    }
}
