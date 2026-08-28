using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CoffeeShop.ApiTests;

public sealed class CorrelationTests
{
    private const string HeaderName = "X-Correlation-ID";

    [Fact]
    public async Task Request_receives_a_server_owned_correlation_id()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var clientCorrelationId = Guid.NewGuid().ToString("D");
        client.DefaultRequestHeaders.Add(HeaderName, clientCorrelationId);

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = Assert.Single(response.Headers.GetValues(HeaderName));
        Assert.True(Guid.TryParseExact(correlationId, "D", out _));
        Assert.NotEqual(clientCorrelationId, correlationId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task Malformed_or_oversized_inbound_correlation_is_rejected(string value)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(HeaderName, value);

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains(HeaderName));
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
        });
}
