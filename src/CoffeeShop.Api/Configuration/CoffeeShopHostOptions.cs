namespace CoffeeShop.Api.Configuration;

public sealed class CoffeeShopHostOptions
{
    public const string SectionName = "CoffeeShopHost";

    public string? PostgreSqlConnectionString { get; set; }

    public string? RedisConnectionString { get; set; }

    public string ClientOrigin { get; set; } = "http://localhost:5173";

    public string? FulfillmentCacheTimeToLive { get; set; }

    public TimeSpan? ParsedFulfillmentCacheTimeToLive =>
        TimeSpan.TryParse(FulfillmentCacheTimeToLive, out var value) ? value : null;
}
