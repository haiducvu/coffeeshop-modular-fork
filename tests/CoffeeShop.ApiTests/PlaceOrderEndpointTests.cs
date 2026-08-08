using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CoffeeShop.ApiTests;

public sealed class PlaceOrderEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PlaceOrderEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Post_order_returns_ok_for_the_original_contract()
    {
        var request = new
        {
            commandType = 0,
            orderSource = 0,
            location = 0,
            loyaltyMemberId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            baristaItems = new[] { new { itemType = 0 } },
            kitchenItems = new[] { new { itemType = 6 } },
            timestamp = DateTimeOffset.Parse("2026-08-08T08:00:00Z")
        };

        using var response = await _client.PostAsJsonAsync("/v1/api/orders", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
