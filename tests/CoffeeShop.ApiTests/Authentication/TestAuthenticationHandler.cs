using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.ApiTests.Authentication;

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "Test";
    internal const string AuthorizationValue = "Test deterministic-ticket";
    internal const string CustomerAuthorizationValue = "Test customer";
    internal const string OtherCustomerAuthorizationValue = "Test other-customer";
    internal const string FulfillmentReaderAuthorizationValue = "Test fulfillment-reader";
    internal const string OperatorAuthorizationValue = "Test operator";

    internal static readonly Guid CustomerLoyaltyMemberId =
        Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

    internal static readonly Guid OtherCustomerLoyaltyMemberId =
        Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorizationValue = Request.Headers.Authorization.ToString();
        if (authorizationValue is not (
            AuthorizationValue
            or CustomerAuthorizationValue
            or OtherCustomerAuthorizationValue
            or FulfillmentReaderAuthorizationValue
            or OperatorAuthorizationValue))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Claim[] claims = authorizationValue switch
        {
            CustomerAuthorizationValue =>
            [
                new Claim("sub", CustomerLoyaltyMemberId.ToString()),
                new Claim(ClaimTypes.Role, "customer")
            ],
            OtherCustomerAuthorizationValue =>
            [
                new Claim("sub", OtherCustomerLoyaltyMemberId.ToString()),
                new Claim(ClaimTypes.Role, "customer")
            ],
            FulfillmentReaderAuthorizationValue =>
            [
                new Claim("sub", "c0a80137-6c8d-4c2a-9860-322e5f32de31"),
                new Claim(ClaimTypes.Role, "fulfillment-reader")
            ],
            OperatorAuthorizationValue =>
            [
                new Claim("sub", "ae28f5c2-1054-4f8d-9e04-e0edacba1f10"),
                new Claim(ClaimTypes.Role, "operator")
            ],
            _ =>
            [
                new Claim("sub", "lesson-17-user"),
                new Claim("scope", "orders:read orders:write"),
                new Claim(ClaimTypes.Role, "barista"),
                new Claim(ClaimTypes.Role, "manager")
            ]
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
