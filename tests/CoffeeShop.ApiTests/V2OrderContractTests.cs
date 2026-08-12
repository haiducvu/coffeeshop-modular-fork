using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;
using CoffeeShop.ApiTests.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CoffeeShop.ApiTests;

public sealed class V2OrderContractTests : IClassFixture<CoffeeShopApiFactory>
{
    private readonly HttpClient _client;

    public V2OrderContractTests(CoffeeShopApiFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
            TestAuthenticationHandler.CustomerAuthorizationValue);
    }

    [Fact]
    public async Task Post_order_creates_a_resource_that_can_be_retrieved()
    {
        var request = new
        {
            orderSource = 0,
            location = 0,
            loyaltyMemberId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            baristaItems = new[] { 0 },
            kitchenItems = new[] { 6 }
        };

        using var createResponse = await _client.PostAsJsonAsync("/v2/orders", request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var location = Assert.Single(createResponse.Headers.GetValues("Location"));
        using var createdDocument = JsonDocument.Parse(
            await createResponse.Content.ReadAsStringAsync());
        var orderId = createdDocument.RootElement.GetProperty("orderId").GetGuid();
        Assert.Equal($"/v2/orders/{orderId}", location);
        Assert.Equal("InProgress", createdDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            $"/v2/orders/{orderId}",
            createdDocument.RootElement.GetProperty("links").GetProperty("self").GetString());

        using var getResponse = await _client.GetAsync(location);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var retrievedDocument = JsonDocument.Parse(
            await getResponse.Content.ReadAsStringAsync());
        Assert.Equal(orderId, retrievedDocument.RootElement.GetProperty("orderId").GetGuid());
        Assert.Equal("InProgress", retrievedDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            location,
            retrievedDocument.RootElement.GetProperty("links").GetProperty("self").GetString());
    }

    [Fact]
    public async Task Get_order_returns_not_found_when_the_resource_does_not_exist()
    {
        using var response = await _client.GetAsync(
            "/v2/orders/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_v1_order_keeps_the_original_ok_contract()
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
