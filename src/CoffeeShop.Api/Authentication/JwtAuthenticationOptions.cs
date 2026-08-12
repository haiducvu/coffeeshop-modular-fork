namespace CoffeeShop.Api.Authentication;

public sealed class JwtAuthenticationOptions
{
    public const string SectionName = "Authentication";
    public bool Enabled { get; init; }
    public required string Authority { get; init; }
    public required string Audience { get; init; }
    public bool RequireHttpsMetadata { get; init; } = true;
}
