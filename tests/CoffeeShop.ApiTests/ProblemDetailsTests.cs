using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeeShop.Api.Errors;
using CoffeeShop.ApiTests.Authentication;
using FluentValidation;
using FluentValidation.Results;

namespace CoffeeShop.ApiTests;

public sealed class ProblemDetailsTests : IClassFixture<CoffeeShopApiFactory>
{
    private readonly HttpClient _client;

    public ProblemDetailsTests(CoffeeShopApiFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = CustomerAuthorization;
    }

    [Fact]
    public async Task Post_v2_order_validation_failure_returns_a_deterministic_problem_details_response()
    {
        var request = new
        {
            orderSource = -1,
            location = -1,
            loyaltyMemberId = Guid.Empty,
            baristaItems = Array.Empty<int>(),
            kitchenItems = Array.Empty<int>()
        };

        using var response = await _client.PostAsJsonAsync("/v2/orders", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertValidationProblemAsync(response);
    }

    [Fact]
    public async Task Get_v2_missing_order_returns_a_not_found_problem_details_response()
    {
        using var response = await _client.GetAsync(
            "/v2/orders/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemAsync(response, "/problems/order-not-found", "Order not found.", 404);
    }

    [Fact]
    public async Task Get_v2_order_not_found_exception_returns_a_not_found_problem_details_response()
    {
        using var factory = new ThrowingCounterModuleFactory(
            new OrderNotFoundException(Guid.Parse("00000000-0000-0000-0000-000000000000")));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = CustomerAuthorization;

        using var response = await client.GetAsync(
            "/v2/orders/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemAsync(response, "/problems/order-not-found", "Order not found.", 404);
    }

    [Fact]
    public async Task Post_v2_order_concurrency_exception_returns_a_conflict_problem_details_response()
    {
        using var factory = new ThrowingCounterModuleFactory(
            new OrderConcurrencyException("Concurrent write conflict."));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = CustomerAuthorization;

        using var response = await client.PostAsJsonAsync("/v2/orders", new
        {
            orderSource = 0,
            location = 0,
            loyaltyMemberId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            baristaItems = new[] { 0 },
            kitchenItems = new[] { 6 }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertProblemAsync(response, "/problems/order-conflict", "Order conflict.", 409);
    }

    [Fact]
    public async Task Post_v2_order_validation_exception_sorts_message_arrays_deterministically()
    {
        var validationException = new ValidationException(
        [
            new ValidationFailure("Items", "Zeta validation failure."),
            new ValidationFailure("BaristaItems[1]", "Item must be eligible."),
            new ValidationFailure("Items", "Alpha validation failure.")
        ]);
        using var factory = new ThrowingCounterModuleFactory(validationException);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = CustomerAuthorization;

        using var response = await client.PostAsJsonAsync("/v2/orders", new
        {
            orderSource = 0,
            location = 0,
            loyaltyMemberId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            baristaItems = new[] { 0 },
            kitchenItems = new[] { 6 }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemAsync(response, "/problems/validation", "Validation failed.", 400);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.Equal(
            ["BaristaItems[1]", "Items"],
            errors.EnumerateObject().Select(error => error.Name).ToArray());
        Assert.Equal(
            ["Item must be eligible."],
            errors.GetProperty("BaristaItems[1]").EnumerateArray()
                .Select(message => message.GetString()!).ToArray());
        Assert.Equal(
            ["Alpha validation failure.", "Zeta validation failure."],
            errors.GetProperty("Items").EnumerateArray()
                .Select(message => message.GetString()!).ToArray());
        Assert.All(
            errors.EnumerateObject().SelectMany(error => error.Value.EnumerateArray()),
            message => Assert.False(string.IsNullOrWhiteSpace(message.GetString())));
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response)
    {
        await AssertProblemAsync(response, "/problems/validation", "Validation failed.", 400);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.Equal(
            ["Items", "Location", "LoyaltyMemberId", "OrderSource"],
            errors.EnumerateObject().Select(error => error.Name).ToArray());
        Assert.All(
            errors.EnumerateObject(),
            error => Assert.NotEmpty(error.Value.EnumerateArray()));
    }

    private static readonly System.Net.Http.Headers.AuthenticationHeaderValue CustomerAuthorization =
        System.Net.Http.Headers.AuthenticationHeaderValue.Parse(
            TestAuthenticationHandler.CustomerAuthorizationValue);

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        string type,
        string title,
        int status)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.ToString());

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement;
        Assert.Equal(type, problem.GetProperty("type").GetString());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
