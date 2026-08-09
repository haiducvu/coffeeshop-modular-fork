using System.Net;
using CoffeeShop.DataGen;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeeShop.DataGenTests;

public sealed class OrderGeneratorWorkerTests
{
    [Fact]
    public async Task Order_count_limits_the_number_of_api_calls()
    {
        var handler = new RecordingHandler(_ => HttpStatusCode.OK);
        var worker = CreateWorker(handler, orderCount: 3);

        await worker.RunAsync(CancellationToken.None);

        Assert.Equal(3, handler.RequestBodies.Count);
        Assert.All(handler.RequestBodies, body => Assert.Contains("baristaItems", body));
    }

    [Fact]
    public async Task Cancellation_stops_the_worker_cleanly()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler(_ =>
        {
            cancellation.Cancel();
            return HttpStatusCode.OK;
        });
        var worker = CreateWorker(handler, orderCount: 10);

        await worker.RunAsync(cancellation.Token);

        Assert.Single(handler.RequestBodies);
    }

    [Fact]
    public async Task Non_success_responses_are_bounded_by_order_count()
    {
        var handler = new RecordingHandler(_ => HttpStatusCode.ServiceUnavailable);
        var worker = CreateWorker(handler, orderCount: 2);

        await worker.RunAsync(CancellationToken.None);

        Assert.Equal(2, handler.RequestBodies.Count);
    }

    private static OrderGeneratorWorker CreateWorker(RecordingHandler handler, int orderCount)
    {
        var clientFactory = new StubHttpClientFactory(
            new HttpClient(handler) { BaseAddress = new Uri("http://coffee-shop.test") });
        var options = Options.Create(new OrderGeneratorOptions
        {
            ApiBaseUrl = new Uri("http://coffee-shop.test"),
            OrderCount = orderCount,
            Interval = TimeSpan.Zero,
            Seed = 42
        });

        return new OrderGeneratorWorker(
            clientFactory,
            options,
            new RandomOrderFactory(options.Value.Seed, TimeProvider.System),
            new ImmediateDelay(),
            new StubApplicationLifetime(),
            NullLogger<OrderGeneratorWorker>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<int, HttpStatusCode> statusForCall) : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(statusForCall(RequestBodies.Count));
        }
    }

    private sealed class ImmediateDelay : IOrderGenerationDelay
    {
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
