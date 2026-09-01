using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace CoffeeShop.ApiTests;

public sealed class OpenTelemetryConfigurationTests
{
    [Fact]
    public async Task Telemetry_providers_work_without_an_exporter_endpoint()
    {
        await using var factory = CreateFactory(otlpEndpoint: string.Empty);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        Assert.NotNull(factory.Services.GetService<TracerProvider>());
        Assert.NotNull(factory.Services.GetService<MeterProvider>());
    }

    [Theory]
    [InlineData("collector:4317")]
    [InlineData("file:///tmp/telemetry")]
    [InlineData("https://collector.example/path")]
    [InlineData("https://user@collector.example")]
    public void Non_canonical_otlp_endpoint_is_rejected_at_startup(string endpoint)
    {
        using var factory = CreateFactory(endpoint);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("OpenTelemetry:OtlpEndpoint", exception.ToString(), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(string otlpEndpoint) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("OpenTelemetry:OtlpEndpoint", otlpEndpoint);
        });
}
