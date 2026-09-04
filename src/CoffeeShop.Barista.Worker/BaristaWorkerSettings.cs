namespace CoffeeShop.Barista.Worker;

public sealed record BaristaWorkerSettings(
    string PostgreSqlConnectionString,
    Uri? OtlpEndpoint);
