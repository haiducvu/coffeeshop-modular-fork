using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CoffeeShop.Api.Health;

namespace CoffeeShop.ApiTests;

public sealed class HealthSemanticsTests
{
    [Fact]
    public async Task Liveness_stays_healthy_while_readiness_reports_a_failed_dependency()
    {
        await using var factory = CreateFactory(services =>
            services.AddHealthChecks().AddCheck(
                "controlled-dependency",
                () => HealthCheckResult.Unhealthy(
                    "secret diagnostic",
                    new InvalidOperationException("secret exception")),
                tags: ["ready"]));
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");
        var readyBody = await ready.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        using var document = JsonDocument.Parse(readyBody);
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        var check = Assert.Single(document.RootElement.GetProperty("checks").EnumerateArray());
        Assert.Equal("controlled-dependency", check.GetProperty("name").GetString());
        Assert.Equal("Unhealthy", check.GetProperty("status").GetString());
        Assert.True(check.GetProperty("durationMilliseconds").GetDouble() >= 0);
        Assert.DoesNotContain("secret diagnostic", readyBody, StringComparison.Ordinal);
        Assert.DoesNotContain("secret exception", readyBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabled_optional_dependencies_are_excluded_from_readiness()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(document.RootElement.GetProperty("checks").EnumerateArray());
    }

    [Fact]
    public async Task Enabled_identity_discovery_failure_makes_readiness_unavailable()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "true");
            builder.UseSetting(
                "Authentication:Authority",
                "https://identity.test/realms/coffeeshop");
            builder.UseSetting("Authentication:Audience", "coffeeshop-api");
            builder.ConfigureServices(services => services
                .AddHttpClient(IdentityProviderReadinessHealthCheck.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new StaticResponseHandler(HttpStatusCode.ServiceUnavailable)));
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("identity-provider", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kafka_failure_affects_readiness_but_not_liveness_when_enabled()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("Messaging:Kafka:Enabled", "true");
            builder.UseSetting("Messaging:Kafka:BootstrapServers", "127.0.0.1:1");
            builder.UseSetting("Messaging:Kafka:TopicPrefix", "coffeeshop");
            builder.UseSetting("Messaging:Kafka:ConsumerGroupPrefix", "coffeeshop");
        });
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");
        var readyBody = await ready.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Contains("kafka", readyBody, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configureServices = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
