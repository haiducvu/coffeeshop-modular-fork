using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CoffeeShop.ApiTests;

public sealed class PlaceOrderEndpointTests : IClassFixture<CoffeeShopApiFactory>
{
    private readonly HttpClient _client;

    public PlaceOrderEndpointTests(CoffeeShopApiFactory factory)
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

    [Fact]
    public async Task Post_order_accepts_valid_barista_and_kitchen_item_types()
    {
        var loyaltyMemberId = Guid.NewGuid();
        var request = new
        {
            commandType = 0,
            orderSource = 0,
            location = 0,
            loyaltyMemberId,
            baristaItems = new[] { new { itemType = 0 } },
            kitchenItems = new[] { new { itemType = 7 } },
            timestamp = DateTimeOffset.Parse("2026-08-08T08:00:00Z")
        };

        using var response = await _client.PostAsJsonAsync("/v1/api/orders", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_order_rejects_an_unknown_item_type()
    {
        var request = new
        {
            commandType = 0,
            orderSource = 0,
            location = 0,
            loyaltyMemberId = Guid.NewGuid(),
            baristaItems = new[] { new { itemType = 999 } },
            kitchenItems = Array.Empty<object>(),
            timestamp = DateTimeOffset.Parse("2026-08-08T08:00:00Z")
        };

        using var response = await _client.PostAsJsonAsync("/v1/api/orders", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_order_rejects_an_empty_loyalty_member_before_persistence()
    {
        var request = new
        {
            commandType = 0,
            orderSource = 0,
            location = 0,
            loyaltyMemberId = Guid.Empty,
            baristaItems = new[] { new { itemType = 0 } },
            kitchenItems = Array.Empty<object>(),
            timestamp = DateTimeOffset.Parse("2026-08-08T08:00:00Z")
        };

        using var response = await _client.PostAsJsonAsync("/v1/api/orders", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
