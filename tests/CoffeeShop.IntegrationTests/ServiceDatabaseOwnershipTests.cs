using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Kitchen;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CoffeeShop.IntegrationTests;

public sealed class ServiceDatabaseOwnershipTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("bootstrap")
        .WithUsername("bootstrap_admin")
        .WithPassword("bootstrap-tests-only")
        .WithEnvironment("COUNTER_DB_PASSWORD", "counter-tests-only")
        .WithEnvironment("BARISTA_DB_PASSWORD", "barista-tests-only")
        .WithEnvironment("KITCHEN_DB_PASSWORD", "kitchen-tests-only")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CoffeeShop.slnx")))
            root = root.Parent;
        var script = Path.Combine(root!.FullName, "deploy/postgres/init-service-databases.sh");
        Assert.True(File.Exists(script), "The service database bootstrap must exist.");
        await _postgres.CopyAsync(await File.ReadAllBytesAsync(script), "/tmp/bootstrap-services.sh");
        // Exercise idempotence against the exact deployment script, not test-only grants.
        for (var run = 0; run < 2; run++)
        {
            var result = await _postgres.ExecAsync(["sh", "/tmp/bootstrap-services.sh"]);
            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain("tests-only", result.Stdout + result.Stderr);
        }
    }

    [Theory]
    [InlineData("counter", "coffeeshop_counter")]
    [InlineData("barista", "coffeeshop_barista")]
    [InlineData("kitchen", "coffeeshop_kitchen")]
    public async Task Each_owner_migrates_and_queries_only_its_database(string owner, string database)
    {
        var connectionString = ConnectionString(owner, database);
        var services = new ServiceCollection();
        services.AddLogging();
        switch (owner)
        {
            case "counter": services.AddCounterModule(connectionString); break;
            case "barista": services.AddBaristaModule(connectionString); break;
            case "kitchen": services.AddKitchenModule(connectionString); break;
        }
        await using var provider = services.BuildServiceProvider();
        switch (owner)
        {
            case "counter": await provider.MigrateCounterModuleAsync(); break;
            case "barista": await provider.MigrateBaristaModuleAsync(); break;
            case "kitchen": await provider.MigrateKitchenModuleAsync(); break;
        }
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {owner}.outbox_messages";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT current_database()";
        Assert.Equal(database, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls FROM pg_roles WHERE rolname = current_user";
        Assert.Equal(false, await command.ExecuteScalarAsync());
        foreach (var foreignOwner in new[] { "counter", "barista", "kitchen" }.Where(name => name != owner))
        {
            command.CommandText = $"SELECT * FROM {foreignOwner}.outbox_messages";
            var missing = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteReaderAsync());
            Assert.Equal(PostgresErrorCodes.UndefinedTable, missing.SqlState);
            await using var forbidden = new NpgsqlConnection(ConnectionString(owner, $"coffeeshop_{foreignOwner}"));
            var denied = await Assert.ThrowsAsync<PostgresException>(() => forbidden.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
    }

    private string ConnectionString(string owner, string database) => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
    {
        Database = database,
        Username = $"coffeeshop_{owner}",
        Password = $"{owner}-tests-only",
        Pooling = false
    }.ConnectionString;

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}
