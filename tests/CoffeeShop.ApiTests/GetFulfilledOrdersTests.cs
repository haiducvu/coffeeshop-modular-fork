using System.Net;
using System.Net.Http.Json;

namespace CoffeeShop.ApiTests;

public sealed class GetFulfilledOrdersTests(CoffeeShopApiFactory factory)
    : IClassFixture<CoffeeShopApiFactory>
{
    [Fact]
    public async Task Get_fulfilled_orders_returns_an_empty_array_when_none_are_fulfilled()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/api/fulfillment-orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var orders = await response.Content.ReadFromJsonAsync<object[]>();
        Assert.Empty(Assert.IsType<object[]>(orders));
    }
}
