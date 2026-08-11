using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeeShop.Api.Errors;
using CoffeeShop.Modules.Counter;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CoffeeShop.ApiTests;

public sealed class UnexpectedFailureTests
{
    [Fact]
    public async Task Post_v2_order_unexpected_failure_returns_a_safe_problem_details_response()
    {
        const string sensitiveMessage =
            "Invalid database connection string: Server=coffee-db;Password=super-secret;token=abc;payload={customer}";
        using var factory = new ThrowingCounterModuleFactory(
            new InvalidOperationException(sensitiveMessage));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/v2/orders", ValidOrderRequest());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.ToString());

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=coffee-db", body, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("token=abc", body, StringComparison.Ordinal);
        Assert.DoesNotContain("payload={customer}", body, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        Assert.Equal("/problems/internal", problem.GetProperty("type").GetString());
        Assert.Equal("An unexpected error occurred.", problem.GetProperty("title").GetString());
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.False(problem.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task Post_v2_order_unexpected_failure_logs_the_response_trace_id()
    {
        var failure = new InvalidOperationException("Unexpected test failure.");
        var logCapture = new LogCapture();
        using var factory = new ThrowingCounterModuleFactory(failure, logCapture);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/v2/orders", ValidOrderRequest());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var traceId = document.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));

        var entry = Assert.Single(logCapture.Entries, entry =>
            entry.CategoryName == typeof(CoffeeShopExceptionHandler).FullName &&
            entry.LogLevel == LogLevel.Error);
        Assert.Same(failure, entry.Exception);
        Assert.Contains(traceId, entry.Message, StringComparison.Ordinal);
    }

    private static object ValidOrderRequest() => new
    {
        orderSource = 0,
        location = 0,
        loyaltyMemberId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
        baristaItems = new[] { 0 },
        kitchenItems = new[] { 6 }
    };
}

internal sealed class ThrowingCounterModuleFactory(
    Exception exception,
    LogCapture? logCapture = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICounterModule>();
            services.AddSingleton<ICounterModule>(new ThrowingCounterModule(exception));
            if (logCapture is not null)
            {
                services.AddSingleton<ILoggerProvider>(logCapture);
            }
        });
    }
}

internal sealed class ThrowingCounterModule(Exception exception) : ICounterModule
{
    public Task<PlaceOrderResult> PlaceOrderAsync(
        PlaceOrderInput input,
        CancellationToken cancellationToken) =>
        Task.FromException<PlaceOrderResult>(exception);

    public Task<IReadOnlyList<FulfilledOrder>> GetFulfilledOrdersAsync(
        CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<FulfilledOrder>>(exception);

    public Task<OrderDetails?> GetOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken) =>
        Task.FromException<OrderDetails?>(exception);
}

internal sealed class LogCapture : ILoggerProvider
{
    private readonly List<LogEntry> _entries = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void Dispose()
    {
    }

    private void Add(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }
    }

    private sealed class CapturingLogger(string categoryName, LogCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            capture.Add(new LogEntry(categoryName, logLevel, formatter(state, exception), exception));
        }
    }
}

internal sealed record LogEntry(
    string CategoryName,
    LogLevel LogLevel,
    string Message,
    Exception? Exception);
