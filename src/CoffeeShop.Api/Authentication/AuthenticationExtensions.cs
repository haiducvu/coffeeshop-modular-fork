using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CoffeeShop.Api.Authentication;

internal static class AuthenticationExtensions
{
    internal static bool AddCoffeeShopAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtAuthenticationOptions.SectionName);
        if (!section.GetValue<bool>(nameof(JwtAuthenticationOptions.Enabled)))
        {
            return false;
        }

        services.AddOptions<JwtAuthenticationOptions>()
            .Bind(section)
            .Validate(
                static options => Uri.TryCreate(
                    options.Authority,
                    UriKind.Absolute,
                    out var authority)
                    && (authority.Scheme == Uri.UriSchemeHttps
                        || authority.Scheme == Uri.UriSchemeHttp),
                "Authentication:Authority must be an absolute HTTP or HTTPS URI.")
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.Audience),
                "Authentication:Audience is required.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtAuthenticationOptions>>((bearer, configured) =>
            {
                var options = configured.Value;
                bearer.Authority = options.Authority;
                bearer.Audience = options.Audience;
                bearer.RequireHttpsMetadata = options.RequireHttpsMetadata;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        services.AddAuthorization();

        return true;
    }
}
