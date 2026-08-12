using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeeShop.ApiTests.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CoffeeShop.ApiTests;

public sealed class AuthorizationMatrixTests : IClassFixture<CoffeeShopApiFactory>
{
    private readonly CoffeeShopApiFactory _factory;

    public AuthorizationMatrixTests(CoffeeShopApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_v2_order_creation_is_challenged()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/v2/orders", ValidOrderRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_non_customer_cannot_create_a_v2_order()
    {
        using var client = CreateClient(TestAuthenticationHandler.AuthorizationValue);

        using var response = await client.PostAsJsonAsync("/v2/orders", ValidOrderRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_can_create_a_v2_order()
    {
        using var client = CreateClient(TestAuthenticationHandler.CustomerAuthorizationValue);

        using var response = await client.PostAsJsonAsync("/v2/orders", ValidOrderRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_v2_order_read_is_challenged()
    {
        var orderId = await CreateCustomerOrderAsync();
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/v2/orders/{orderId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customer_can_read_their_own_order()
    {
        var orderId = await CreateCustomerOrderAsync();
        using var client = CreateClient(TestAuthenticationHandler.CustomerAuthorizationValue);

        using var response = await client.GetAsync($"/v2/orders/{orderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_read_another_customers_order()
    {
        var orderId = await CreateCustomerOrderAsync();
        using var client = CreateClient(TestAuthenticationHandler.OtherCustomerAuthorizationValue);

        using var response = await client.GetAsync($"/v2/orders/{orderId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Operator_can_read_any_customers_order()
    {
        var orderId = await CreateCustomerOrderAsync();
        using var client = CreateClient(TestAuthenticationHandler.OperatorAuthorizationValue);

        using var response = await client.GetAsync($"/v2/orders/{orderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(TestAuthenticationHandler.FulfillmentReaderAuthorizationValue)]
    [InlineData(TestAuthenticationHandler.OperatorAuthorizationValue)]
    public async Task Fulfillment_reader_or_operator_can_read_the_v2_queue(string authorizationValue)
    {
        using var client = CreateClient(authorizationValue);

        using var response = await client.GetAsync("/v2/fulfillment-orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_customer_cannot_read_the_v2_fulfillment_queue()
    {
        using var client = CreateClient(TestAuthenticationHandler.CustomerAuthorizationValue);

        using var response = await client.GetAsync("/v2/fulfillment-orders");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_operations_order_read_is_challenged()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/v2/operations/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Fulfillment_reader_cannot_read_an_operations_order()
    {
        using var client = CreateClient(TestAuthenticationHandler.FulfillmentReaderAuthorizationValue);

        using var response = await client.GetAsync($"/v2/operations/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Operator_can_read_an_operations_order()
    {
        var orderId = await CreateCustomerOrderAsync();
        using var client = CreateClient(TestAuthenticationHandler.OperatorAuthorizationValue);

        using var response = await client.GetAsync($"/v2/operations/orders/{orderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_v1_order_creation_remains_available()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/v1/api/orders", new
        {
            commandType = 0,
            orderSource = 0,
            location = 0,
            loyaltyMemberId = TestAuthenticationHandler.CustomerLoyaltyMemberId,
            baristaItems = new[] { new { itemType = 0 } },
            kitchenItems = new[] { new { itemType = 6 } },
            timestamp = DateTimeOffset.Parse("2026-08-08T08:00:00Z")
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_authentication_does_not_expose_v2_routes()
    {
        await using var factory = new DisabledAuthenticationApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/v2/orders", ValidOrderRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClient(string authorizationValue)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(authorizationValue);
        return client;
    }

    private async Task<Guid> CreateCustomerOrderAsync()
    {
        using var client = CreateClient(TestAuthenticationHandler.CustomerAuthorizationValue);
        using var response = await client.PostAsJsonAsync("/v2/orders", ValidOrderRequest());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("orderId").GetGuid();
    }

    private static object ValidOrderRequest() => new
    {
        orderSource = 0,
        location = 0,
        loyaltyMemberId = TestAuthenticationHandler.CustomerLoyaltyMemberId,
        baristaItems = new[] { 0 },
        kitchenItems = new[] { 6 }
    };

    private sealed class DisabledAuthenticationApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
        }
    }
}
