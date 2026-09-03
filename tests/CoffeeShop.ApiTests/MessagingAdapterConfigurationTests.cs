using CoffeeShop.Messaging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.ApiTests;

public sealed class MessagingAdapterConfigurationTests
{
    private const string AppApiToken = "lesson-30-test-app-token";

    [Fact]
    public void Undefined_messaging_adapter_fails_at_startup()
    {
        using var factory = CreateFactory("RabbitMq");

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Messaging:Adapter", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("Kafka or Dapr", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dapr_selection_registers_the_Dapr_publisher_without_Kafka_configuration()
    {
        await using var factory = CreateFactory("Dapr");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        var publisher = factory.Services.GetRequiredService<IIntegrationEventPublisher>();

        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "CoffeeShop.Messaging.Dapr",
            publisher.GetType().Assembly.GetName().Name);
    }

    [Fact]
    public async Task Dapr_callback_rejects_requests_without_the_app_channel_token()
    {
        await using var factory = CreateFactory("Dapr");
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/dapr/orders/v1",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dapr_subscription_discovery_rejects_the_wrong_app_channel_token()
    {
        await using var factory = CreateFactory("Dapr");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("dapr-api-token", "incorrect-token-value");

        using var response = await client.GetAsync("/dapr/subscribe");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_Dapr_CloudEvent_is_acknowledged_as_Drop()
    {
        await using var factory = CreateFactory("Dapr");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("dapr-api-token", AppApiToken);
        const string malformedCloudEvent =
            """
            {
              "specversion": "1.0",
              "id": "malformed-message",
              "source": "lesson-30-test",
              "type": "coffeeshop.orders.placed.v1",
              "datacontenttype": "application/json",
              "data": {
                "messageId": "not-a-guid"
              }
            }
            """;

        using var response = await client.PostAsync(
            "/dapr/orders/v1",
            new StringContent(
                malformedCloudEvent,
                Encoding.UTF8,
                "application/cloudevents+json"));

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DROP", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invalid_Dapr_CloudEvent_JSON_is_acknowledged_as_Drop()
    {
        await using var factory = CreateFactory("Dapr");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("dapr-api-token", AppApiToken);

        using var response = await client.PostAsync(
            "/dapr/orders/v1",
            new StringContent(
                "{ \"specversion\": \"1.0\", ",
                Encoding.UTF8,
                "application/cloudevents+json"));

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DROP", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Dapr_selection_exposes_only_the_two_version_one_subscriptions()
    {
        await using var factory = CreateFactory("Dapr");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("dapr-api-token", AppApiToken);

        using var response = await client.GetAsync("/dapr/subscribe");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var subscriptions = document.RootElement.EnumerateArray()
            .Select(element => new
            {
                PubSubName = element.GetProperty("pubsubName").GetString(),
                Topic = element.GetProperty("topic").GetString(),
                Route = ReadRoute(element)
            })
            .OrderBy(subscription => subscription.Topic, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, subscriptions.Length);
        Assert.Collection(
            subscriptions,
            subscription =>
            {
                Assert.Equal("coffeeshop-pubsub", subscription.PubSubName);
                Assert.Equal("coffeeshop.orders.v1", subscription.Topic);
                Assert.Equal("dapr/orders/v1", subscription.Route);
            },
            subscription =>
            {
                Assert.Equal("coffeeshop-pubsub", subscription.PubSubName);
                Assert.Equal("coffeeshop.preparation.v1", subscription.Topic);
                Assert.Equal("dapr/preparation/v1", subscription.Route);
            });
    }

    [Fact]
    public async Task Disabled_Kafka_mode_does_not_expose_Dapr_subscriptions()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("Messaging:Adapter", "Kafka");
            builder.UseSetting("Messaging:Kafka:Enabled", "false");
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/dapr/subscribe");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void Dapr_rejects_a_non_canonical_gRPC_sidecar_endpoint()
    {
        using var factory = CreateFactory("Dapr").WithWebHostBuilder(builder =>
            builder.UseSetting(
                "Messaging:Dapr:SidecarGrpcEndpoint",
                "dapr-sidecar:50001/path"));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "Dapr sidecar gRPC endpoint",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Dapr_requires_an_app_channel_token_at_startup()
    {
        using var factory = CreateFactory("Dapr").WithWebHostBuilder(builder =>
            builder.UseSetting("Messaging:Dapr:AppApiToken", string.Empty));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Dapr app API token", exception.ToString(), StringComparison.Ordinal);
    }

    private static string? ReadRoute(JsonElement subscription) =>
        subscription.TryGetProperty("route", out var route)
            ? route.GetString()
            : subscription.GetProperty("routes").GetProperty("default").GetString();

    private static WebApplicationFactory<Program> CreateFactory(string adapter) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("Messaging:Adapter", adapter);
            builder.UseSetting("Messaging:Kafka:Enabled", "true");
            builder.UseSetting("Messaging:Kafka:BootstrapServers", string.Empty);
            builder.UseSetting("Messaging:Dapr:PubSubName", "coffeeshop-pubsub");
            builder.UseSetting("Messaging:Dapr:TopicPrefix", "coffeeshop");
            builder.UseSetting("Messaging:Dapr:AppApiToken", AppApiToken);
            builder.UseSetting(
                "Messaging:Dapr:SidecarHttpEndpoint",
                "http://127.0.0.1:3500");
            builder.UseSetting(
                "Messaging:Dapr:SidecarGrpcEndpoint",
                "http://127.0.0.1:50001");
        });
}
