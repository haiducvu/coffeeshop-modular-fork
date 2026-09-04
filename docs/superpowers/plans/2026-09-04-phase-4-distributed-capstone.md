# Phase 4 Distributed Capstone Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract Barista and Kitchen into independently deployable .NET 10 workers, enforce service-owned persistence, prove the distributed Kafka workflow, describe a Nomad deployment, and audit the complete 36-lesson curriculum.

**Architecture:** Use a staged strangler extraction. The API chooses `Embedded` or `External` hosting per station; Kafka Compose uses external workers while Dapr remains an explicit embedded alternate. Existing IntegrationContracts, Kafka adapter, module-owned Outbox/Inbox, retry/DLT, correlation, and OpenTelemetry seams are reused without synchronous service calls or contract redesign.

**Tech Stack:** .NET 10 Generic Host, ASP.NET Core Minimal APIs, EF Core 10/Npgsql/PostgreSQL, xUnit, Testcontainers, Confluent.Kafka, Avro/Schema Registry, OpenTelemetry, Docker Compose, Dapr regression profile, and Nomad HCL.

**Spec:** `docs/superpowers/specs/2026-09-04-phase-4-distributed-capstone-design.md`

## Global Constraints

- Target `net10.0` and keep package versions in `Directory.Packages.props`.
- Preserve `/v1`, `/v2`, SignalR, authorization, Redis cache, DataGen, Kafka, and Dapr observable behavior.
- Keep domain/application code independent from host, EF Core, Kafka, Dapr, Redis, and telemetry implementations.
- Use Kafka integration events across processes and MediatR/domain events only inside one process.
- Keep at-least-once plus Inbox idempotency semantics; never claim distributed transactions or exactly-once.
- Each lesson is one coherent green commit with a Vietnamese lesson document and English commit subject.
- Push each lesson immediately after its complete verification gate; never rewrite published lesson history.
- Lesson 31 stops after Barista extraction; do not add Kitchen Worker files until Lesson 32.
- Kafka is the Phase 4 distributed transport. Dapr stays an explicit embedded regression topology.
- Keep secrets, payloads, loyalty identities, credentials, and connection strings out of source control and logs.

## File and interface map

Lesson 31 introduces these stable names:

- `CoffeeShop.Api.Configuration.ModuleHostingMode` — `Embedded` or `External`.
- `ConfigurationExtensions.ResolveModuleHosting(IConfiguration, string)` — validated API composition choice.
- `CoffeeShop.Barista.Worker.BaristaWorkerServiceCollectionExtensions.AddBaristaWorker(IServiceCollection, IConfiguration)` — complete Barista process composition.
- `CoffeeShop.Barista.Worker.BaristaWorkerSettings` — validated `ConnectionStrings:Barista` and OTLP endpoint.
- `CoffeeShop.Barista.Worker.Time.TaskPreparationDelay` — worker-local production delay adapter.
- `scripts/phase-4-barista-smoke.sh` — Lesson 31 process and workflow proof.

Lesson 32 mirrors only the public hosting pattern with Kitchen-specific types. It must not introduce a shared station worker framework. Lesson 33 replaces shared physical persistence with three logical databases. Lessons 34–36 build only on these published interfaces.

---

### Task 1: Lesson 31 — Extract Barista into a worker service

**Files:**

- Create: `src/CoffeeShop.Barista.Worker/CoffeeShop.Barista.Worker.csproj`
- Create: `src/CoffeeShop.Barista.Worker/Program.cs`
- Create: `src/CoffeeShop.Barista.Worker/BaristaWorkerSettings.cs`
- Create: `src/CoffeeShop.Barista.Worker/BaristaWorkerServiceCollectionExtensions.cs`
- Create: `src/CoffeeShop.Barista.Worker/Time/TaskPreparationDelay.cs`
- Create: `src/CoffeeShop.Barista.Worker/Telemetry/BaristaWorkerOpenTelemetryExtensions.cs`
- Create: `src/CoffeeShop.Barista.Worker/appsettings.json`
- Create: `src/CoffeeShop.Barista.Worker/Dockerfile`
- Create: `tests/CoffeeShop.WorkerTests/CoffeeShop.WorkerTests.csproj`
- Create: `tests/CoffeeShop.WorkerTests/BaristaWorkerConfigurationTests.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/ExtractedBaristaWorkflowTests.cs`
- Create: `scripts/phase-4-barista-smoke.sh`
- Create: `tests/scripts/phase-4-barista-fakes/docker`
- Create: `tests/scripts/phase-4-barista-fakes/phase-3-smoke.sh`
- Create: `tests/scripts/phase-4-barista-smoke-tests.sh`
- Create: `docs/lessons/31-extract-barista-worker.md`
- Modify: `CoffeeShop.slnx`
- Create: `src/CoffeeShop.Api/Configuration/ModuleHostingMode.cs`
- Modify: `src/CoffeeShop.Api/Configuration/ConfigurationExtensions.cs`
- Modify: `src/CoffeeShop.Api/Program.cs`
- Modify: `src/CoffeeShop.Api/appsettings.json`
- Modify: `compose.yaml`
- Modify: `scripts/phase-3-smoke.sh`
- Modify: `.github/workflows/ci.yml`
- Modify: `tests/CoffeeShop.ApiTests/ConfigurationValidationTests.cs`
- Modify: `tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj`
- Modify: `tests/CoffeeShop.ArchitectureTests/CoffeeShop.ArchitectureTests.csproj`
- Modify: `tests/CoffeeShop.ArchitectureTests/ModuleDependencyTests.cs`
- Modify: `README.md`

**Interfaces:**

- Consumes: `AddBaristaModule(string, Action<BaristaOutboxOptions>?)`, `MigrateBaristaModuleAsync`, `AddKafkaMessaging`, `AddKafkaConsumer<OrderPlacedV1>("barista")`, `IPreparationDelay`, and the Phase 3 Outbox/Inbox contracts.
- Produces: `ModuleHostingMode`, `ResolveModuleHosting`, `AddBaristaWorker`, the `barista-worker` Compose service, and a process-level smoke contract reused by Lessons 32–36.

- [ ] **Step 1: Add a failing API configuration test for explicit hosting modes**

Add these tests to `ConfigurationValidationTests.cs`:

```csharp
[Theory]
[InlineData(null, ModuleHostingMode.Embedded)]
[InlineData("Embedded", ModuleHostingMode.Embedded)]
[InlineData("external", ModuleHostingMode.External)]
public void Barista_hosting_mode_is_resolved_explicitly(
    string? configuredValue,
    ModuleHostingMode expected)
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Modules:Barista:Hosting"] = configuredValue
        })
        .Build();

    Assert.Equal(
        expected,
        configuration.ResolveModuleHosting("Barista"));
}

[Fact]
public void Undefined_barista_hosting_mode_is_rejected()
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Modules:Barista:Hosting"] = "Shadow"
        })
        .Build();

    var exception = Assert.Throws<OptionsValidationException>(() =>
        configuration.ResolveModuleHosting("Barista"));

    Assert.Contains("Modules:Barista:Hosting", exception.Message, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused API test and verify RED**

Run:

```bash
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj \
  --filter 'FullyQualifiedName~Barista_hosting_mode|FullyQualifiedName~Undefined_barista_hosting_mode'
```

Expected: compile failure because `ModuleHostingMode` and `ResolveModuleHosting` do not exist. This is the production change the test guards: silently accepting an unknown deployment mode.

- [ ] **Step 3: Implement the minimal hosting-mode parser**

Create `ModuleHostingMode.cs`:

```csharp
namespace CoffeeShop.Api.Configuration;

public enum ModuleHostingMode
{
    Embedded,
    External
}
```

Add to `ConfigurationExtensions`:

```csharp
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

    if (!Enum.TryParse<ModuleHostingMode>(value, true, out var mode)
        || !Enum.IsDefined(mode))
    {
        throw new OptionsValidationException(
            key,
            typeof(ModuleHostingMode),
            [$"{key} must be Embedded or External."]);
    }

    return mode;
}
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Step 2 command. Expected: all selected cases pass with no warnings.

- [ ] **Step 5: Add the Worker project shell and failing configuration/integration tests**

Create `CoffeeShop.Barista.Worker.csproj` with the exact XML in Step 7 but no C# implementation yet. Create
`CoffeeShop.WorkerTests.csproj` with `net10.0`, xUnit packages, and project references to Barista Worker,
Messaging Abstractions, and Modules.Barista. Add a Barista Worker project reference to
`CoffeeShop.Messaging.IntegrationTests` and `CoffeeShop.ArchitectureTests`. Add both new projects to
`CoffeeShop.slnx`.

Create `BaristaWorkerConfigurationTests.cs`:

```csharp
using CoffeeShop.Barista.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoffeeShop.WorkerTests;

public sealed class BaristaWorkerConfigurationTests
{
    [Fact]
    public void Missing_barista_connection_string_is_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            new ServiceCollection().AddBaristaWorker(configuration));

        Assert.Contains("ConnectionStrings:Barista", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsafe_otel_endpoint_is_rejected_without_echoing_it()
    {
        const string unsafeEndpoint = "https://collector.example/path?token=secret-value";
        var configuration = ValidConfiguration(new Dictionary<string, string?>
        {
            ["OpenTelemetry:OtlpEndpoint"] = unsafeEndpoint
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            new ServiceCollection().AddBaristaWorker(configuration));

        Assert.Contains("OpenTelemetry:OtlpEndpoint", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", exception.ToString(), StringComparison.Ordinal);
    }
}
```

Add this literal helper in the test class; it must not derive expected values from production helpers:

```csharp
private static IConfiguration ValidConfiguration(
    IReadOnlyDictionary<string, string?> overrides)
{
    var settings = new Dictionary<string, string?>
    {
        ["ConnectionStrings:Barista"] =
            "Host=localhost;Database=coffeeshop;Username=coffeeshop;Password=local-only",
        ["Messaging:Kafka:BootstrapServers"] = "localhost:9092",
        ["Messaging:Kafka:SchemaRegistryUrl"] = "http://localhost:8081",
        ["Messaging:Kafka:ProducerFormat"] = "Json",
        ["Messaging:Kafka:TopicPrefix"] = "lesson31",
        ["Messaging:Kafka:ConsumerGroupPrefix"] = "lesson31"
    };
    foreach (var pair in overrides)
    {
        settings[pair.Key] = pair.Value;
    }

    return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
```

Also create `ExtractedBaristaWorkflowTests.cs`. Reuse `KafkaFixture`, `OutboxPostgreSqlFixture`, literal
unique topic/group prefixes, and the no-delay test adapter. Build two independent `IHost` instances:

```csharp
var applicationBuilder = Host.CreateApplicationBuilder();
applicationBuilder.Services.AddKafkaMessaging(ConfigureKafka);
applicationBuilder.Services.AddCounterModule(postgres.ConnectionString, configureOutbox: ConfigureCounterOutbox);
applicationBuilder.Services.AddKitchenModule(postgres.ConnectionString, ConfigureKitchenOutbox);
applicationBuilder.Services.AddKafkaConsumer<OrderPlacedV1>("kitchen");
applicationBuilder.Services.AddKafkaConsumer<OrderItemPreparedV1>("counter");

var baristaBuilder = Host.CreateApplicationBuilder();
baristaBuilder.Services.AddSingleton<IPreparationDelay, NoPreparationDelay>();
baristaBuilder.Configuration.AddInMemoryCollection(BaristaConfiguration());
baristaBuilder.Services.AddBaristaWorker(baristaBuilder.Configuration);
```

Migrate Counter/Kitchen from the application host and Barista from the worker host. Start both, place one
mixed order through `ICounterModule`, wait for fulfillment, then assert these literal counts:

```csharp
Assert.Equal(new long[] { 1, 1, 1, 1, 2 }, await ReadCountsAsync(cancellationToken));
```

The fields are Barista items, Barista Inbox, published Barista Outbox, Kitchen items, and Counter Inbox.
Always stop both hosts in `finally`, worker first.

- [ ] **Step 6: Run the Worker and split-host tests and verify RED**

Run:

```bash
dotnet test tests/CoffeeShop.WorkerTests/CoffeeShop.WorkerTests.csproj \
  --filter FullyQualifiedName~BaristaWorkerConfigurationTests
dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj \
  --filter FullyQualifiedName~ExtractedBaristaWorkflowTests
```

Expected: compile failure because `AddBaristaWorker` does not exist. This failure proves both tests depend
on the missing worker composition boundary rather than reassembling Barista from test-only services.

- [ ] **Step 7: Implement the Worker composition boundary**

The worker project shell from Step 5 contains:

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CoffeeShop.IntegrationContracts\CoffeeShop.IntegrationContracts.csproj" />
    <ProjectReference Include="..\CoffeeShop.Messaging.Kafka\CoffeeShop.Messaging.Kafka.csproj" />
    <ProjectReference Include="..\CoffeeShop.Modules.Barista\CoffeeShop.Modules.Barista.csproj" />
    <ProjectReference Include="..\CoffeeShop.SharedKernel\CoffeeShop.SharedKernel.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
  </ItemGroup>
</Project>
```

`BaristaWorkerSettings` must expose only validated values:

```csharp
public sealed record BaristaWorkerSettings(
    string PostgreSqlConnectionString,
    Uri? OtlpEndpoint);
```

`AddBaristaWorker` must validate the connection with `NpgsqlConnectionStringBuilder`, validate an optional canonical HTTP(S) OTLP origin without reflecting the configured value, register the settings, call `AddKafkaMessaging`, register a worker-local `IPreparationDelay`, call `AddBaristaModule` with `Messaging:BaristaOutbox`, and register exactly `AddKafkaConsumer<OrderPlacedV1>("barista")`.

Use `TryAddSingleton<IPreparationDelay, TaskPreparationDelay>()` so integration tests can install a no-delay implementation before composition. Configure telemetry with:

```csharp
services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "coffeeshop-barista-worker",
        serviceNamespace: "CoffeeShop"))
    .WithTracing(tracing => tracing
        .AddSource(MessagingTelemetry.ActivitySourceName)
        .AddEntityFrameworkCoreInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter(MessagingTelemetry.MeterName)
        .AddRuntimeInstrumentation());
```

Only add OTLP exporters when `settings.OtlpEndpoint` is non-null.

Use this worker appsettings shape so direct execution is embedded nowhere and fails clearly until the user
provides a database connection:

```json
{
  "ConnectionStrings": {
    "Barista": ""
  },
  "OpenTelemetry": {
    "OtlpEndpoint": ""
  },
  "Messaging": {
    "Kafka": {
      "BootstrapServers": "localhost:9092",
      "SchemaRegistryUrl": "http://localhost:8081",
      "ProducerFormat": "Avro",
      "TopicPrefix": "coffeeshop",
      "ConsumerGroupPrefix": "coffeeshop"
    },
    "BaristaOutbox": {
      "BatchSize": 20,
      "PollInterval": "00:00:01",
      "LeaseDuration": "00:00:30",
      "RetryDelay": "00:00:05"
    }
  }
}
```

- [ ] **Step 8: Implement startup migration and cooperative host lifecycle**

Create `Program.cs`:

```csharp
using CoffeeShop.Barista.Worker;
using CoffeeShop.Modules.Barista;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBaristaWorker(builder.Configuration);
using var host = builder.Build();
await host.Services.MigrateBaristaModuleAsync();
await host.RunAsync();

public partial class Program;
```

Do not catch and downgrade configuration or migration failures. A failed migration must terminate before Kafka consumption starts.

- [ ] **Step 9: Run Worker configuration and split-host tests and verify GREEN**

Run both Step 6 commands. Expected: configuration behaviors and the real Kafka split-host workflow pass
without warnings.

- [ ] **Step 10: Add a failing API test for the unsupported Dapr/external combination**

Add to `ConfigurationValidationTests.cs`:

```csharp
[Fact]
public void Dapr_with_external_barista_fails_startup()
{
    using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:Enabled", "false");
        builder.UseSetting("Messaging:Kafka:Enabled", "true");
        builder.UseSetting("Messaging:Adapter", "Dapr");
        builder.UseSetting("Modules:Barista:Hosting", "External");
    });

    var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

    Assert.Contains(
        "Dapr requires Modules:Barista:Hosting to be Embedded",
        exception.ToString(),
        StringComparison.Ordinal);
}
```

- [ ] **Step 11: Run the Dapr/external test and verify RED**

Run:

```bash
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj \
  --filter FullyQualifiedName~Dapr_with_external_barista_fails_startup
```

Expected: `Assert.ThrowsAny` fails because current API accepts this unsafe dual-topology configuration. The
test guards an external mode that has no Dapr sidecar/HTTP subscription host in Phase 4.

- [ ] **Step 12: Cut API composition over only when Barista is External**

In `Program.cs`, resolve the mode once:

```csharp
var baristaHosting = builder.Configuration.ResolveModuleHosting("Barista");
if (messagingAdapter == MessagingAdapter.Dapr
    && baristaHosting == ModuleHostingMode.External)
{
    throw new InvalidOperationException(
        "Dapr requires Modules:Barista:Hosting to be Embedded.");
}
```

Register `AddKafkaConsumer<OrderPlacedV1>("barista")`, `AddBaristaModule`, and `MigrateBaristaModuleAsync` only when `baristaHosting == ModuleHostingMode.Embedded`. Do not change Counter or Kitchen registration. Add `Modules:Barista:Hosting = Embedded` to API appsettings.

- [ ] **Step 13: Run focused API, Worker, and split-host tests and verify GREEN**

Run the Step 2, Step 6, and Step 11 commands. Expected: all pass.

- [ ] **Step 14: Add Docker/Compose topology and its failing behavior test**

Create a multi-stage worker Dockerfile using `mcr.microsoft.com/dotnet/sdk:10.0-alpine` to publish and
`mcr.microsoft.com/dotnet/runtime:10.0-alpine` as non-root final image. Copy only Worker,
IntegrationContracts, Messaging Abstractions/Kafka, Barista module, Contracts, and SharedKernel project
graphs. The final stage must install `krb5-libs` for the Confluent client and run as `$APP_UID`:

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CoffeeShop.Barista.Worker/CoffeeShop.Barista.Worker.csproj src/CoffeeShop.Barista.Worker/
COPY src/CoffeeShop.Contracts/CoffeeShop.Contracts.csproj src/CoffeeShop.Contracts/
COPY src/CoffeeShop.IntegrationContracts/CoffeeShop.IntegrationContracts.csproj src/CoffeeShop.IntegrationContracts/
COPY src/CoffeeShop.Messaging.Abstractions/CoffeeShop.Messaging.Abstractions.csproj src/CoffeeShop.Messaging.Abstractions/
COPY src/CoffeeShop.Messaging.Kafka/CoffeeShop.Messaging.Kafka.csproj src/CoffeeShop.Messaging.Kafka/
COPY src/CoffeeShop.Modules.Barista/CoffeeShop.Modules.Barista.csproj src/CoffeeShop.Modules.Barista/
COPY src/CoffeeShop.SharedKernel/CoffeeShop.SharedKernel.csproj src/CoffeeShop.SharedKernel/
RUN dotnet restore src/CoffeeShop.Barista.Worker/CoffeeShop.Barista.Worker.csproj
COPY src/CoffeeShop.Barista.Worker/ src/CoffeeShop.Barista.Worker/
COPY src/CoffeeShop.Contracts/ src/CoffeeShop.Contracts/
COPY src/CoffeeShop.IntegrationContracts/ src/CoffeeShop.IntegrationContracts/
COPY src/CoffeeShop.Messaging.Abstractions/ src/CoffeeShop.Messaging.Abstractions/
COPY src/CoffeeShop.Messaging.Kafka/ src/CoffeeShop.Messaging.Kafka/
COPY src/CoffeeShop.Modules.Barista/ src/CoffeeShop.Modules.Barista/
COPY src/CoffeeShop.SharedKernel/ src/CoffeeShop.SharedKernel/
RUN dotnet publish src/CoffeeShop.Barista.Worker/CoffeeShop.Barista.Worker.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
RUN apk add --no-cache krb5-libs
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CoffeeShop.Barista.Worker.dll"]
```

Add `barista-worker` to Compose with:

```yaml
barista-worker:
  build:
    context: .
    dockerfile: src/CoffeeShop.Barista.Worker/Dockerfile
  environment:
    ConnectionStrings__Barista: Host=postgres;Port=5432;Database=${POSTGRES_DB:-coffeeshop};Username=${POSTGRES_USER:-coffeeshop};Password=${POSTGRES_PASSWORD:-coffeeshop-local}
    Messaging__Kafka__Enabled: "true"
    Messaging__Kafka__BootstrapServers: ${KAFKA_BOOTSTRAP_SERVERS:-kafka:19092}
    Messaging__Kafka__SchemaRegistryUrl: ${SCHEMA_REGISTRY_URL:-http://schema-registry:8081}
    Messaging__Kafka__ProducerFormat: ${KAFKA_PRODUCER_FORMAT:-Avro}
    Messaging__Kafka__TopicPrefix: ${KAFKA_TOPIC_PREFIX:-coffeeshop}
    Messaging__Kafka__ConsumerGroupPrefix: ${KAFKA_CONSUMER_GROUP_PREFIX:-coffeeshop}
    OpenTelemetry__OtlpEndpoint: ${OTEL_EXPORTER_OTLP_ENDPOINT:-}
  depends_on:
    postgres:
      condition: service_healthy
    kafka:
      condition: service_healthy
    schema-registry:
      condition: service_healthy
  restart: on-failure:3
```

Set API `Modules__Barista__Hosting` from `${BARISTA_HOSTING_MODE:-External}`. Dapr commands must explicitly set `BARISTA_HOSTING_MODE=Embedded` and must not start `barista-worker`.

Write `phase-4-barista-smoke.sh` so it first requires `barista-worker` in `docker compose ps --status running --services`, then delegates to `scripts/phase-3-smoke.sh`. Write fake behavior tests proving a missing worker fails and a running worker delegates exactly once. Run the test before implementation and expect failure because the script is absent.

- [ ] **Step 15: Verify Compose workflow and regressions**

Run:

```bash
./tests/scripts/phase-4-barista-smoke-tests.sh
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka schema-registry api barista-worker signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
./scripts/phase-4-barista-smoke.sh
```

Expected: one external Barista process completes the mixed order; Phase 3 persistence deltas and correlation proof pass.

Run Dapr embedded regression separately:

```bash
docker compose --profile dapr down --volumes --remove-orphans
BARISTA_HOSTING_MODE=Embedded MESSAGING_ADAPTER=Dapr \
  docker compose --profile dapr up -d --build postgres redis kafka api dapr-sidecar
BARISTA_HOSTING_MODE=Embedded MESSAGING_ADAPTER=Dapr ./scripts/phase-3-smoke.sh
docker compose --profile dapr down --volumes --remove-orphans
```

- [ ] **Step 16: Update architecture/CI/documentation**

Architecture tests must load the Worker assembly and assert it does not depend on `CoffeeShop.Api`, Counter, Kitchen, or Dapr. CI Kafka, identity, DataGen, and observability startup lists must include `barista-worker`; the Dapr job must set `BARISTA_HOSTING_MODE=Embedded` and omit it.

Write `docs/lessons/31-extract-barista-worker.md` in Vietnamese with sections:

- Mục đích bài học.
- Vertical slice trước/sau extraction.
- Vì sao explicit `Embedded|External` tránh dual ownership.
- Worker startup, migration, cancellation và Outbox/Inbox flow.
- Schema ownership hôm nay và physical database isolation ở Lesson 33.
- Kafka distributed path versus Dapr embedded regression.
- Cách chạy test/smoke.
- Summary kiến thức.

Add Lesson 31 to README and state Lesson 32 has not started.

- [ ] **Step 17: Run the Lesson 31 complete gate**

Run:

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --configuration Release --no-restore
dotnet test CoffeeShop.slnx --configuration Release --no-build
docker compose --profile demo --profile identity --profile observability --profile dapr config --quiet
docker compose --profile demo --profile identity --profile observability --profile dapr build
./tests/scripts/phase-3-smoke-tests.sh
./tests/scripts/phase-4-barista-smoke-tests.sh
git diff --check
```

Then run fresh Kafka, Dapr embedded, identity, DataGen, and observability proofs. Expected: all commands exit zero, no build warnings, no skipped test caused by Lesson 31, and no Kitchen Worker file exists.

- [ ] **Step 18: Commit, push, and verify only Lesson 31**

```bash
git add .
git commit -m "lesson(31): extract barista into a worker service" \
  -m "Purpose: Move Barista consumption, persistence, migrations, and Outbox publication into an independently runnable .NET 10 worker while preserving embedded Dapr compatibility." \
  -m "Verification: Split-host Kafka integration tests, worker configuration tests, architecture rules, fresh Compose workflows, adapter regressions, and the full solution gate pass." \
  -m "Knowledge: Covers strangler service extraction, explicit process ownership, worker lifecycle, migration startup, deployment-independent contracts, and avoiding dual consumers."
git push origin learning/dotnet10-rebuild
git rev-parse HEAD
git ls-remote origin refs/heads/learning/dotnet10-rebuild
```

Expected: local and remote hashes match; stop without creating Lesson 32 files.

---

### Task 2: Lesson 32 — Extract Kitchen into a worker service

**Files:**

- Create: `src/CoffeeShop.Kitchen.Worker/CoffeeShop.Kitchen.Worker.csproj`
- Create: `src/CoffeeShop.Kitchen.Worker/Program.cs`
- Create: `src/CoffeeShop.Kitchen.Worker/KitchenWorkerSettings.cs`
- Create: `src/CoffeeShop.Kitchen.Worker/KitchenWorkerServiceCollectionExtensions.cs`
- Create: `src/CoffeeShop.Kitchen.Worker/Time/TaskPreparationDelay.cs`
- Create: `src/CoffeeShop.Kitchen.Worker/Telemetry/KitchenWorkerOpenTelemetryExtensions.cs`
- Create: `src/CoffeeShop.Kitchen.Worker/appsettings.json`
- Create: `src/CoffeeShop.Kitchen.Worker/Dockerfile`
- Create: `tests/CoffeeShop.WorkerTests/KitchenWorkerConfigurationTests.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/ExtractedKitchenWorkflowTests.cs`
- Create: `scripts/phase-4-kitchen-smoke.sh`
- Create: `docs/lessons/32-extract-kitchen-worker.md`
- Modify: `CoffeeShop.slnx`, `src/CoffeeShop.Api/Program.cs`, `src/CoffeeShop.Api/appsettings.json`, `compose.yaml`, `.github/workflows/ci.yml`, architecture tests, worker tests, and `README.md`.

**Interfaces:**

- Consumes: `ModuleHostingMode`, `ResolveModuleHosting`, Kafka worker composition pattern, and Kitchen module public registration/migration methods.
- Produces: `AddKitchenWorker(IServiceCollection, IConfiguration)`, `kitchen-worker` service, and complete three-process Kafka topology.

- [ ] **Step 1: Write and run RED configuration tests**

Add `KitchenWorkerConfigurationTests` that calls
`new ServiceCollection().AddKitchenWorker(configuration)` and asserts a missing
`ConnectionStrings:Kitchen` produces `OptionsValidationException` containing only that key. Add API theory
cases proving `ResolveModuleHosting("Kitchen")` maps missing/`Embedded` to `Embedded`, maps `external` to
`External`, and rejects literal `Shadow`. Run focused Worker/API tests; expect compile failure because
`AddKitchenWorker` is absent.

- [ ] **Step 2: Implement Kitchen worker without a shared station framework**

Use `Host.CreateApplicationBuilder`, worker-local `TaskPreparationDelay`, service name `coffeeshop-kitchen-worker`, `AddKitchenModule`, `MigrateKitchenModuleAsync`, and exactly:

```csharp
services.AddKafkaConsumer<OrderPlacedV1>("kitchen");
```

Do not reference Barista Worker or Barista module.

- [ ] **Step 3: Write/run RED then GREEN split-host integration proof**

Start API/Counter host, Barista host, and Kitchen host independently. Place one mixed order and assert one item/inbox/outbox effect per station and two Counter Inbox rows. Stop workers before the application host in `finally`.

- [ ] **Step 4: Cut API/Compose topology over and prove it**

When Kitchen is `External`, API omits Kitchen module, migration, consumer, and Outbox worker. Compose defaults both stations to External and starts both workers. Dapr explicitly sets both modes Embedded and starts neither worker. Run fresh Kafka, Dapr, identity, DataGen, and observability proofs.

- [ ] **Step 5: Document, full-gate, commit, and push**

```bash
git commit -m "lesson(32): extract kitchen into a worker service" \
  -m "Purpose: Move Kitchen reliability and preparation into its own process without sharing Barista internals." \
  -m "Verification: Split-host tests, duplicate handling, Compose workflows, Dapr regression, and full gates pass." \
  -m "Knowledge: Covers repeatable extraction patterns, bounded duplication, independent composition, and service lifecycle."
git push origin learning/dotnet10-rebuild
```

---

### Task 3: Lesson 33 — Enforce independent service data ownership

**Files:**

- Create: `deploy/postgres/init-service-databases.sh`
- Create: `tests/CoffeeShop.IntegrationTests/ServiceDatabaseOwnershipTests.cs`
- Create: `docs/architecture/service-data-ownership.md`
- Create: `docs/lessons/33-service-data-ownership.md`
- Modify: worker/API connection settings, Compose volumes/environment, smoke SQL targets, architecture tests, CI, and README.

**Interfaces:**

- Consumes: service-specific `ConnectionStrings:CoffeeShop|Barista|Kitchen` and module-owned migration entry points.
- Produces: `coffeeshop_counter`, `coffeeshop_barista`, and `coffeeshop_kitchen` databases with distinct runtime credentials.

- [ ] **Step 1: Write RED database ownership integration tests**

Create three credentials in the PostgreSQL fixture. Assert each can migrate/query its own database and receives PostgreSQL permission denial for another service database. Derive expected database names as literals, not from production config.

- [ ] **Step 2: Add deterministic database bootstrap and least-privilege grants**

The init script must use `set -eu`, create roles/databases only when absent, pass secrets by environment variables, and grant no cross-database `CONNECT`. Never echo passwords.

- [ ] **Step 3: Point each process only at its database**

API migrates Counter only; Barista/Kitchen workers migrate only their databases. Remove shared-schema SQL from Phase 4 smoke and query each owner independently. No cross-database joins.

- [ ] **Step 4: Run RED/GREEN ownership and workflow gates**

Run focused PostgreSQL ownership tests, module migration tests, and a fresh three-database Kafka flow. Stop if any process can query another database.

- [ ] **Step 5: Document, full-gate, commit, and push**

```bash
git commit -m "lesson(33): enforce independent service data ownership" \
  -m "Purpose: Give Counter, Barista, and Kitchen separate PostgreSQL databases, credentials, and migration lifecycles." \
  -m "Verification: Permission-denial tests, per-service migrations, distributed smoke, and full gates pass." \
  -m "Knowledge: Covers database-per-service, least privilege, eventual consistency, and data-boundary enforcement."
git push origin learning/dotnet10-rebuild
```

---

### Task 4: Lesson 34 — Exercise the distributed flow end to end

**Files:**

- Create: `scripts/phase-4-smoke.sh`
- Create: `tests/scripts/phase-4-smoke-tests.sh`
- Create: `scripts/phase-4-fault-demo.sh`
- Create: `docs/runbooks/distributed-failure-demo.md`
- Create: `docs/lessons/34-distributed-flow.md`
- Modify: `compose.yaml`, `.github/workflows/ci.yml`, `README.md`, and Phase 4 smoke fakes.

**Interfaces:**

- Consumes: three-process topology and three service-owned databases.
- Produces: deterministic finite-batch acceptance contract and safe container-lifecycle fault demonstrations.

- [ ] **Step 1: Write RED smoke harness tests**

Fake API, Docker, Kafka offsets, and PostgreSQL responses. Tests must fail for lost orders, duplicate effects, pending/rejected Outbox rows, missing worker, unbounded polling, and failed restart recovery.

- [ ] **Step 2: Implement bounded distributed smoke**

Use one global deadline, finite `DATAGEN_ORDER_COUNT`, unique run correlation, and eventual assertions. Compare per-service deltas, never global fixed totals. Diagnostics must be time-bounded and redact environment values.

- [ ] **Step 3: Implement lifecycle fault proof**

Pause/stop only a named worker container after Counter commits an order, wait for broker backlog, restart it, and assert fulfillment plus one business effect. Do not add a production failure endpoint.

- [ ] **Step 4: Run real fresh and recovery scenarios**

Run normal finite batch, Barista interruption, Kitchen interruption, duplicate Kafka delivery, retry, and poison DLT paths from clean volumes.

- [ ] **Step 5: Document, full-gate, commit, and push**

```bash
git commit -m "lesson(34): exercise the distributed coffee shop flow" \
  -m "Purpose: Turn the three-process topology into a deterministic, fault-aware end-to-end capstone." \
  -m "Verification: Finite batches, worker interruption/recovery, duplicate protection, retry/DLT, and full gates pass." \
  -m "Knowledge: Covers eventual assertions, system testing, safe fault injection, recovery windows, and demo ergonomics."
git push origin learning/dotnet10-rebuild
```

---

### Task 5: Lesson 35 — Deploy the capstone with Nomad

**Files:**

- Create: `deploy/nomad/coffeeshop.nomad.hcl`
- Create: `deploy/nomad/variables.example.hcl`
- Create: `scripts/validate-nomad.sh`
- Create: `tests/scripts/validate-nomad-tests.sh`
- Create: `docs/runbooks/nomad-rollout.md`
- Create: `docs/lessons/35-nomad-deployment.md`
- Modify: CI and README.

**Interfaces:**

- Consumes: immutable API/worker images, per-service settings, and operational probes from the Compose capstone.
- Produces: parameterized Nomad jobs plus deterministic static validation when Nomad CLI is absent.

- [ ] **Step 1: Write RED static-render tests**

Assert rendered jobs contain three distinct task names, image variables, restart/reschedule policies, rolling update stanza, resource bounds, service checks, and no literal local password/token.

- [ ] **Step 2: Implement parameterized Nomad jobs**

Use variables for image tags, Kafka/Schema Registry/OTLP endpoints and database secret references. Put no default production credential in HCL. Add checks that match each process's real observable health surface.

- [ ] **Step 3: Add validation and rollout/rollback runbook**

`validate-nomad.sh` runs static checks always and `nomad job validate` when available. The runbook defines migration ordering, canary/rolling behavior, failure observation, rollback image selection, and the constraint that old consumers must remain contract-compatible.

- [ ] **Step 4: Run validation, full-gate, commit, and push**

```bash
git commit -m "lesson(35): deploy the coffee shop with Nomad" \
  -m "Purpose: Express the distributed capstone as a parameterized, health-aware Nomad deployment with rollback guidance." \
  -m "Verification: Static HCL tests, Nomad validation when available, secret scans, and full gates pass." \
  -m "Knowledge: Covers scheduling, service health, configuration injection, rolling updates, rescheduling, and rollback."
git push origin learning/dotnet10-rebuild
```

---

### Task 6: Lesson 36 — Audit and publish the curriculum

**Files:**

- Create: `scripts/audit-curriculum-history.sh`
- Create: `tests/scripts/audit-curriculum-history-tests.sh`
- Create: `docs/architecture/context.md`
- Create: `docs/architecture/container.md`
- Create: `docs/architecture/component.md`
- Create: `docs/architecture/decisions.md`
- Create: `CONTRIBUTING.md`
- Create: `docs/lessons/36-curriculum-audit.md`
- Create: `docs/checkpoints/phase-4.md`
- Modify: README and CI.

**Interfaces:**

- Consumes: published lesson subjects, docs, checkpoint tags, build/test/smoke scripts, and the final topology.
- Produces: executable 36-lesson history audit, C4 documentation, decision index, contributor workflow, and Phase 4 checkpoint.

- [ ] **Step 1: Write RED history-audit behavior tests**

Create temporary Git fixtures that separately contain a missing lesson, duplicate lesson number, wrong order, missing Vietnamese lesson doc, and allowed operational fix commit. Assert exact bounded failure categories; do not grep only the audit script source.

- [ ] **Step 2: Implement the audit script**

Verify exactly Lessons 01–36 occur once and in order, each subject matches the master map, each doc `docs/lessons/NN-*.md` exists, checkpoint tags point at the intended lesson, the tree has no tracked secret file, and operational commits are reported without being counted as lessons.

- [ ] **Step 3: Add C4/ADR/contributor documentation**

Document system context, container boundaries, component ownership, major decisions and their consequences. Diagrams must match real project/service/database names. Contributor steps must preserve one green lesson commit, Vietnamese lesson docs, English commit subjects, and non-force pushes.

- [ ] **Step 4: Run clean-clone and complete acceptance audit**

Clone to a temporary directory, restore/build/test, build every image, run Kafka distributed smoke plus identity/DataGen/observability/Dapr regression, then run the history audit. Compare local and remote hashes before tagging.

- [ ] **Step 5: Commit, push, tag, and verify**

```bash
git commit -m "lesson(36): complete the curriculum and history audit" \
  -m "Purpose: Publish an executable, documented, and reproducible 36-lesson .NET 10 distributed-systems curriculum." \
  -m "Verification: Clean-clone build/tests, all runtime profiles, history/docs audit, architecture docs, and remote hash checks pass." \
  -m "Knowledge: Covers educational history maintenance, reproducibility, architecture communication, release evidence, and safe publication."
git push origin learning/dotnet10-rebuild
git tag -a phase-4-capstone -m "Phase 4: distributed capstone"
git push origin phase-4-capstone
```

Expected: the learning branch and `phase-4-capstone` tag resolve to the same Lesson 36 commit; no force push occurs.

---

## Plan self-review checklist

- [x] Every section of the approved Phase 4 design maps to one of Lessons 31–36.
- [x] Lesson 31 has exact files, interfaces, RED/GREEN steps, Compose cutover, regressions, docs, commit, and push.
- [x] Lesson 31 keeps Kitchen embedded and creates no Kitchen Worker file.
- [x] Barista has one runtime owner in each topology; no shadow consumer is released.
- [x] Dapr stays explicitly embedded and Kafka stays the distributed reference path.
- [x] Physical database isolation remains in Lesson 33 rather than leaking into Lesson 31.
- [x] Worker, mode, configuration key, consumer role, topic, and service names are consistent.
- [x] Every later lesson consumes interfaces defined by an earlier lesson.
- [x] No incomplete instruction, undefined neighboring interface, exactly-once claim, or secret value remains.
- [x] Each lesson ends with full verification, one lesson commit, immediate push, and remote hash confirmation.
