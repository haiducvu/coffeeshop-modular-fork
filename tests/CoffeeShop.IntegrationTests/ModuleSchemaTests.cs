using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Kitchen;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CoffeeShop.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ModuleSchemaTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Modules_migrate_only_their_owned_tables_into_owned_schemas()
    {
        await fixture.ResetModuleSchemasAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCounterModule(fixture.ConnectionString);
        services.AddBaristaModule(fixture.ConnectionString);
        services.AddKitchenModule(fixture.ConnectionString);
        await using var provider = services.BuildServiceProvider();

        await provider.MigrateCounterModuleAsync();
        await provider.MigrateBaristaModuleAsync();
        await provider.MigrateKitchenModuleAsync();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_schema, table_name
            FROM information_schema.tables
            WHERE table_schema IN ('counter', 'barista', 'kitchen')
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_schema, table_name;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        Assert.Equal(
            [
                "barista.inbox_messages",
                "barista.items",
                "barista.outbox_messages",
                "counter.inbox_messages",
                "counter.line_items",
                "counter.orders",
                "counter.outbox_messages",
                "kitchen.inbox_messages",
                "kitchen.items",
                "kitchen.outbox_messages"
            ],
            tables);
    }
}
