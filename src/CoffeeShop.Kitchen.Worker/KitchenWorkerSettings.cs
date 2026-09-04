namespace CoffeeShop.Kitchen.Worker;

public sealed record KitchenWorkerSettings(
    string PostgreSqlConnectionString,
    Uri? OtlpEndpoint);
