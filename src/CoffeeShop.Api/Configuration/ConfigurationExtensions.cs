using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace CoffeeShop.Api.Configuration;

public static class ConfigurationExtensions
{
    private static readonly TimeSpan MinimumCacheTimeToLive = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumCacheTimeToLive = TimeSpan.FromHours(1);

    public static CoffeeShopHostOptions AddCoffeeShopHostOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireDatabase)
    {
        var configuredOptions = new CoffeeShopHostOptions
        {
            PostgreSqlConnectionString = configuration.GetConnectionString("CoffeeShop"),
            RedisConnectionString = configuration.GetConnectionString("Redis"),
            ClientOrigin = configuration["ClientOrigin"] ?? "http://localhost:5173",
            FulfillmentCacheTimeToLive = configuration["FulfillmentCache:TimeToLive"]
        };
        var failures = Validate(configuredOptions, requireDatabase).ToArray();
        if (failures.Length > 0)
        {
            throw new OptionsValidationException(
                CoffeeShopHostOptions.SectionName,
                typeof(CoffeeShopHostOptions),
                failures);
        }

        services.AddSingleton(configuredOptions);

        return configuredOptions;
    }

    public static ModuleHostingMode ResolveModuleHosting(
        this IConfiguration configuration,
        string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        var key = $"Modules:{moduleName}:Hosting";
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return ModuleHostingMode.Embedded;
        }

        if (!Enum.TryParse<ModuleHostingMode>(value, ignoreCase: true, out var mode)
            || !Enum.IsDefined(mode))
        {
            throw new OptionsValidationException(
                key,
                typeof(ModuleHostingMode),
                [$"{key} must be Embedded or External."]);
        }

        return mode;
    }

    private static IEnumerable<string> Validate(
        CoffeeShopHostOptions options,
        bool requireDatabase)
    {
        if (requireDatabase && !IsValidPostgreSqlConnectionString(options.PostgreSqlConnectionString))
        {
            yield return "ConnectionStrings:CoffeeShop is required and must be a valid PostgreSQL connection string.";
        }

        if (!IsValidRedisConnectionString(options.RedisConnectionString))
        {
            yield return "ConnectionStrings:Redis must be a valid Redis connection string when configured.";
        }

        if (!IsCanonicalHttpOrigin(options.ClientOrigin))
        {
            yield return "ClientOrigin must be an absolute HTTP or HTTPS URI.";
        }

        if (!IsValidCacheTimeToLive(options.FulfillmentCacheTimeToLive))
        {
            yield return "FulfillmentCache:TimeToLive must be a TimeSpan between 5 seconds and 1 hour when configured.";
        }
    }

    private static bool IsValidPostgreSqlConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrWhiteSpace(builder.Host)
                && !string.IsNullOrWhiteSpace(builder.Database)
                && !string.IsNullOrWhiteSpace(builder.Username);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return false;
        }
    }

    private static bool IsValidRedisConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return true;
        }

        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            return options.EndPoints.Count > 0;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return false;
        }
    }

    private static bool IsValidCacheTimeToLive(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return true;
        }

        return TimeSpan.TryParse(configuredValue, out var timeToLive)
            && timeToLive >= MinimumCacheTimeToLive
            && timeToLive <= MaximumCacheTimeToLive;
    }

    private static bool IsCanonicalHttpOrigin(string configuredValue)
    {
        return Uri.TryCreate(configuredValue, UriKind.Absolute, out var origin)
            && (origin.Scheme == Uri.UriSchemeHttp || origin.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(origin.UserInfo)
            && string.Equals(
                configuredValue,
                origin.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase);
    }
}
