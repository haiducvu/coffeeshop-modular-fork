using System.Net;

namespace CoffeeShop.ApiTests;

public sealed class HealthEndpointTests(CoffeeShopApiFactory factory)
    : IClassFixture<CoffeeShopApiFactory>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoint_reports_healthy(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
