using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CoffeeShop.ApiTests.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CoffeeShop.ApiTests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Anonymous_request_is_challenged_without_an_authenticated_principal()
    {
        await using var factory = new CoffeeShopApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v2/authentication");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deterministic_test_ticket_exposes_subject_scopes_and_roles()
    {
        await using var factory = new CoffeeShopApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            AuthenticationHeaderValue.Parse(TestAuthenticationHandler.AuthorizationValue);

        using var response = await client.GetAsync("/v2/authentication");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var identity = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(identity);
        Assert.Equal("lesson-17-user", identity.Subject);
        Assert.Equal(["orders:read", "orders:write"], identity.Scopes);
        Assert.Equal(["barista", "manager"], identity.Roles);
    }

    [Fact]
    public async Task Expired_real_jwt_is_rejected_by_the_bearer_handler()
    {
        await using var factory = new RealJwtApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            JwtBearerDefaults.AuthenticationScheme,
            RealJwtApiFactory.CreateExpiredToken());

        using var response = await client.GetAsync("/v2/authentication");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_real_jwt_is_accepted_by_the_bearer_handler()
    {
        await using var factory = new RealJwtApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            JwtBearerDefaults.AuthenticationScheme,
            RealJwtApiFactory.CreateValidToken());

        using var response = await client.GetAsync("/v2/authentication");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Real_jwt_realm_roles_are_mapped_once_to_standard_role_claims()
    {
        await using var factory = new RealJwtApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            JwtBearerDefaults.AuthenticationScheme,
            RealJwtApiFactory.CreateValidTokenWithRealmRoles("customer", "operator"));

        using var response = await client.GetAsync("/v2/authentication");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var identity = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(identity);
        Assert.Equal(["customer", "operator"], identity.Roles);
    }

    [Fact]
    public async Task Real_jwt_with_an_invalid_signature_is_rejected_by_the_bearer_handler()
    {
        await using var factory = new RealJwtApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            JwtBearerDefaults.AuthenticationScheme,
            RealJwtApiFactory.CreateTokenWithInvalidSignature());

        using var response = await client.GetAsync("/v2/authentication");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    public async Task Real_jwt_with_an_invalid_issuer_or_audience_is_rejected_by_the_bearer_handler(
        string invalidField)
    {
        await using var factory = new RealJwtApiFactory();
        using var client = factory.CreateClient();
        var token = invalidField switch
        {
            "issuer" => RealJwtApiFactory.CreateTokenWithInvalidIssuer(),
            "audience" => RealJwtApiFactory.CreateTokenWithInvalidAudience(),
            _ => throw new InvalidOperationException($"Unknown fixture '{invalidField}'.")
        };
        client.DefaultRequestHeaders.Authorization = new(
            JwtBearerDefaults.AuthenticationScheme,
            token);

        using var response = await client.GetAsync("/v2/authentication");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_authentication_registers_no_scheme_and_keeps_phase_1_public()
    {
        await using var factory = new DisabledAuthenticationApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello World!", await response.Content.ReadAsStringAsync());
        Assert.Null(factory.Services.GetService<IAuthenticationSchemeProvider>());
    }

    [Fact]
    public void Enabled_authentication_rejects_an_invalid_authority_at_startup()
    {
        using var factory = new InvalidAuthenticationOptionsApiFactory();

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Authentication:Authority", exception.Message, StringComparison.Ordinal);
    }

    private sealed record AuthenticationResponse(
        string Subject,
        string[] Scopes,
        string[] Roles);

    private sealed class DisabledAuthenticationApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "false");
        }
    }

    private sealed class InvalidAuthenticationOptionsApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "true");
            builder.UseSetting("Authentication:Authority", "not-an-absolute-uri");
            builder.UseSetting("Authentication:Audience", "coffeeshop-api");
        }
    }

    private sealed class RealJwtApiFactory : WebApplicationFactory<Program>
    {
        private const string Issuer = "https://identity.test/realms/coffeeshop";
        private const string Audience = "coffeeshop-api";
        private static readonly SymmetricSecurityKey TrustedSigningKey = new(
            Encoding.UTF8.GetBytes("lesson-17-trusted-signing-key-32-bytes"));
        private static readonly SymmetricSecurityKey UntrustedSigningKey = new(
            Encoding.UTF8.GetBytes("lesson-17-wrong-signing-key--32-bytes"));

        internal static string CreateExpiredToken() => CreateToken(
            TrustedSigningKey,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020, 1, 1, 0, 5, 0, DateTimeKind.Utc));

        internal static string CreateValidToken() => CreateToken(
            TrustedSigningKey,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        internal static string CreateValidTokenWithRealmRoles(params string[] roles) => CreateToken(
            TrustedSigningKey,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            claims: [new Claim("realm_access", JsonSerializer.Serialize(new { roles }))]);

        internal static string CreateTokenWithInvalidSignature() => CreateToken(
            UntrustedSigningKey,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        internal static string CreateTokenWithInvalidIssuer() => CreateToken(
            TrustedSigningKey,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            issuer: "https://untrusted-identity.test/realms/coffeeshop");

        internal static string CreateTokenWithInvalidAudience() => CreateToken(
            TrustedSigningKey,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            audience: "untrusted-api");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Enabled", "true");
            builder.UseSetting("Authentication:Authority", Issuer);
            builder.UseSetting("Authentication:Audience", Audience);
            builder.UseSetting("Authentication:RequireHttpsMetadata", "true");
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        var configuration = new OpenIdConnectConfiguration
                        {
                            Issuer = Issuer
                        };
                        configuration.SigningKeys.Add(TrustedSigningKey);
                        options.Configuration = configuration;
                        options.TokenValidationParameters.ValidIssuer = Issuer;
                        options.TokenValidationParameters.IssuerSigningKey = TrustedSigningKey;
                    }));
        }

        private static string CreateToken(
            SecurityKey signingKey,
            DateTime notBefore,
            DateTime expires,
            string issuer = Issuer,
            string audience = Audience,
            IEnumerable<Claim>? claims = null)
        {
            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: [new Claim("sub", "real-jwt-user"), .. (claims ?? [])],
                notBefore: notBefore,
                expires: expires,
                signingCredentials: new SigningCredentials(
                    signingKey,
                    SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
