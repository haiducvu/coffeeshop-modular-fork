using System.Reflection;
using CoffeeShop.Modules.Barista;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoffeeShop.ApiTests;

public sealed class BaristaHostingCompositionTests
{
    [Fact]
    public void External_mode_removes_barista_module_and_consumer_from_the_api()
    {
        var services = CaptureProductionServices("External");

        Assert.DoesNotContain(services, BelongsToBaristaModule);
        Assert.Equal(0, services.Count(IsBaristaKafkaConsumerStage));
    }

    [Fact]
    public void Embedded_mode_keeps_barista_module_and_all_consumer_stages_in_the_api()
    {
        var services = CaptureProductionServices("Embedded");

        Assert.Contains(services, BelongsToBaristaModule);
        Assert.Equal(3, services.Count(IsBaristaKafkaConsumerStage));
    }

    private static ServiceDescriptor[] CaptureProductionServices(string hostingMode)
    {
        ServiceDescriptor[]? captured = null;
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting(
                "ConnectionStrings:CoffeeShop",
                "Host=localhost;Database=coffeeshop;Username=coffeeshop;Password=local-only");
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("Messaging:Adapter", "Kafka");
            builder.UseSetting("Messaging:Kafka:Enabled", "true");
            builder.UseSetting("Messaging:Kafka:BootstrapServers", "127.0.0.1:9092");
            builder.UseSetting("Messaging:Kafka:ProducerFormat", "Json");
            builder.UseSetting("Modules:Barista:Hosting", hostingMode);
            builder.ConfigureServices(services =>
            {
                captured = services.ToArray();
                throw new CompositionCapturedException();
            });
        });

        _ = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        return Assert.IsType<ServiceDescriptor[]>(captured);
    }

    private static bool BelongsToBaristaModule(ServiceDescriptor descriptor)
    {
        var baristaAssembly = typeof(BaristaModuleServiceCollectionExtensions).Assembly;
        var implementationType = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType
            : descriptor.ImplementationType;
        return UsesAssembly(descriptor.ServiceType, baristaAssembly)
            || UsesAssembly(implementationType, baristaAssembly);
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

    private static bool UsesAssembly(Type? type, Assembly assembly)
    {
        if (type is null)
        {
            return false;
        }

        return type.Assembly == assembly
            || type.IsGenericType
            && type.GetGenericArguments().Any(argument => UsesAssembly(argument, assembly));
    }

    private sealed class CompositionCapturedException : Exception;
}
