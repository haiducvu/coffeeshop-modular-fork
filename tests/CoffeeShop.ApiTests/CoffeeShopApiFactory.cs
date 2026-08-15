using CoffeeShop.ApiTests.Authentication;
using CoffeeShop.Api.Health;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.ApiTests;

public sealed class CoffeeShopApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:Enabled", "true");
        builder.UseSetting(
            "Authentication:Authority",
            "https://identity.test/realms/coffeeshop");
        builder.UseSetting("Authentication:Audience", "coffeeshop-api");
        builder.UseSetting("Authentication:RequireHttpsMetadata", "true");
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(IdentityProviderReadinessHealthCheck.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new HealthyIdentityHandler());
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    private sealed class HealthyIdentityHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
