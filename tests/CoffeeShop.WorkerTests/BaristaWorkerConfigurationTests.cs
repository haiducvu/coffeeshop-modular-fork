using System.Reflection;
using CoffeeShop.Barista.Worker;
using CoffeeShop.Barista.Worker.Logging;
using CoffeeShop.Messaging.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CoffeeShop.WorkerTests;

public sealed class BaristaWorkerConfigurationTests
{
    [Fact]
    public void Missing_barista_connection_string_is_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            new ServiceCollection().AddBaristaWorker(configuration));

        Assert.Contains(
            "ConnectionStrings:Barista",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Unsafe_otel_endpoint_is_rejected_without_echoing_it()
    {
        const string unsafeEndpoint = "https://collector.example/path?token=secret-value";
        var configuration = ValidConfiguration(new Dictionary<string, string?>
        {
            ["OpenTelemetry:OtlpEndpoint"] = unsafeEndpoint
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            new ServiceCollection().AddBaristaWorker(configuration));

        Assert.Contains(
            "OpenTelemetry:OtlpEndpoint",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret-value",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_kafka_bootstrap_servers_is_rejected_before_migration()
    {
        var configuration = ValidConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Kafka:BootstrapServers"] = string.Empty
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBaristaWorker(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.ValidateBaristaWorkerOptions());

        Assert.Contains(
            "Kafka bootstrap servers are required",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_console_logging_is_json_and_includes_scopes()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddBaristaWorkerLogging());
        using var provider = services.BuildServiceProvider();

        var consoleOptions = provider
            .GetRequiredService<IOptions<ConsoleLoggerOptions>>()
            .Value;
        var jsonOptions = provider
            .GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>()
            .CurrentValue;

        Assert.Equal(ConsoleFormatterNames.Json, consoleOptions.FormatterName);
        Assert.True(jsonOptions.IncludeScopes);
        Assert.True(jsonOptions.UseUtcTimestamp);
        Assert.False(jsonOptions.JsonWriterOptions.Indented);
    }

    [Fact]
    public void Worker_registers_exactly_one_logical_barista_consumer_role()
    {
        var services = new ServiceCollection();
        services.AddBaristaWorker(ValidConfiguration(
            new Dictionary<string, string?>()));

        Assert.Equal(3, services.Count(IsBaristaKafkaConsumerStage));
    }

    private static IConfiguration ValidConfiguration(
        IReadOnlyDictionary<string, string?> overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Barista"] =
                "Host=localhost;Database=coffeeshop;Username=coffeeshop;Password=local-only",
            ["Messaging:Kafka:BootstrapServers"] = "localhost:9092",
            ["Messaging:Kafka:SchemaRegistryUrl"] = "http://localhost:8081",
            ["Messaging:Kafka:ProducerFormat"] = "Json",
            ["Messaging:Kafka:TopicPrefix"] = "lesson31",
            ["Messaging:Kafka:ConsumerGroupPrefix"] = "lesson31"
        };
        foreach (var pair in overrides)
        {
            settings[pair.Key] = pair.Value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private static bool IsBaristaKafkaConsumerStage(ServiceDescriptor descriptor)
    {
        if (descriptor.ServiceType != typeof(IHostedService)
            || descriptor.ImplementationFactory?.Target is not { } closure)
        {
            return false;
        }

        return CapturesString(closure, "barista", depth: 0);
    }

    private static bool CapturesString(object closure, string expected, int depth)
    {
        if (depth > 2)
        {
            return false;
        }

        foreach (var field in closure.GetType().GetFields(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var value = field.GetValue(closure);
            if (value is string text
                && string.Equals(text, expected, StringComparison.Ordinal))
            {
                return true;
            }

            if (value is not null
                && field.FieldType.Name.Contains("DisplayClass", StringComparison.Ordinal)
                && CapturesString(value, expected, depth + 1))
            {
                return true;
            }
        }

        return false;
    }
}
