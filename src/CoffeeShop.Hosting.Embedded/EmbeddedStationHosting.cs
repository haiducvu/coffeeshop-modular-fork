using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Barista.Infrastructure.Outbox;
using CoffeeShop.Modules.Kitchen;
using CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.Hosting.Embedded;

// Compatibility composition for direct development and the embedded Dapr topology.
// Distributed hosts never call these registration/migration methods.
public static class EmbeddedStationHosting
{
    public static IServiceCollection AddEmbeddedBarista(
        this IServiceCollection services, string connectionString,
        IConfiguration configuration, bool messagingEnabled) =>
        services.AddBaristaModule(connectionString, messagingEnabled
            ? configuration.GetSection(BaristaOutboxOptions.SectionName).Bind : null);

    public static IServiceCollection AddEmbeddedKitchen(
        this IServiceCollection services, string connectionString,
        IConfiguration configuration, bool messagingEnabled) =>
        services.AddKitchenModule(connectionString, messagingEnabled
            ? configuration.GetSection(KitchenOutboxOptions.SectionName).Bind : null);

    public static Task MigrateEmbeddedBaristaAsync(this IServiceProvider services) =>
        services.MigrateBaristaModuleAsync();

    public static Task MigrateEmbeddedKitchenAsync(this IServiceProvider services) =>
        services.MigrateKitchenModuleAsync();
}
