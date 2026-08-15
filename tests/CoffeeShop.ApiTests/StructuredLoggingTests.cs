using System.Collections.Concurrent;
using System.Text.Json;
using CoffeeShop.Api.Logging;
using CoffeeShop.Api.Features.Orders.V2;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace CoffeeShop.ApiTests;

public sealed class StructuredLoggingTests
{
    [Fact]
    public async Task Request_log_is_json_with_correlation_and_http_fields()
    {
        var sink = new RecordingSink();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
            builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(sink));
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        var requestEvent = Assert.Single(
            sink.Events,
            logEvent => HasScalar(logEvent, "RequestPath", "/"));
        var json = FormatAsJson(requestEvent);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("Timestamp", out _));
        Assert.Equal("Information", document.RootElement.GetProperty("Level").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("RenderedMessage").GetString()));
        var properties = document.RootElement.GetProperty("Properties");
        Assert.Equal("/", properties.GetProperty("RequestPath").GetString());
        Assert.Equal(200, properties.GetProperty("StatusCode").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(properties.GetProperty("TraceId").GetString()));
    }

    [Fact]
    public void Sensitive_values_and_complete_order_payloads_are_redacted()
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration()
            .Destructure.With(new SensitiveDataDestructuringPolicy())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Received {@SensitiveData}", new
        {
            Authorization = "Bearer authorization-secret",
            AccessToken = "token-secret",
            Password = "password-secret",
            ConnectionString = "Host=db;Username=app;Password=connection-secret",
            OrderPayload = new { LoyaltyMemberId = "member-secret", Item = "Latte" },
            Order = new { LoyaltyMemberId = "order-secret", Item = "Mocha" },
            Metadata = new Dictionary<string, string>
            {
                ["Password"] = "dictionary-secret"
            },
            SafeValue = "visible"
        });

        var json = FormatAsJson(Assert.Single(sink.Events));
        Assert.DoesNotContain("authorization-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("connection-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("member-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Latte", json, StringComparison.Ordinal);
        Assert.DoesNotContain("order-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Mocha", json, StringComparison.Ordinal);
        Assert.DoesNotContain("dictionary-secret", json, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
        Assert.Contains("visible", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_formatter_does_not_emit_sensitive_exception_messages()
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration()
            .WriteTo.Sink(sink)
            .CreateLogger();
        logger.Error(
            new InvalidOperationException(
                "ConnectionStrings:CoffeeShop Password=exception-secret"),
            "Database operation failed for {AccessToken}",
            "property-secret");

        using var writer = new StringWriter();
        new SensitiveDataDestructuringPolicy().Format(Assert.Single(sink.Events), writer);
        var json = writer.ToString();

        Assert.DoesNotContain("exception-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("property-secret", json, StringComparison.Ordinal);
        Assert.Contains(typeof(InvalidOperationException).FullName!, json, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Known_order_request_type_is_redacted_under_a_neutral_property_name()
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration()
            .Destructure.With(new SensitiveDataDestructuringPolicy())
            .WriteTo.Sink(sink)
            .CreateLogger();
        var request = new CreateOrderRequest(
            OrderSource: 0,
            Location: 0,
            LoyaltyMemberId: Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            BaristaItems: [5],
            KitchenItems: [6]);

        logger.Information("Received {@Request}", request);

        var json = FormatAsJson(Assert.Single(sink.Events));
        Assert.DoesNotContain(request.LoyaltyMemberId.ToString(), json, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
    }

    private static bool HasScalar(LogEvent logEvent, string name, string expected) =>
        logEvent.Properties.TryGetValue(name, out var value)
        && value is ScalarValue { Value: string actual }
        && string.Equals(actual, expected, StringComparison.Ordinal);

    private static string FormatAsJson(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        new SensitiveDataDestructuringPolicy().Format(logEvent, writer);
        return writer.ToString();
    }

    private sealed class RecordingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
