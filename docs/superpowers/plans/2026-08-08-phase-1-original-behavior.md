# Phase 1 Original Behavior Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconstruct the original CoffeeShop order, Barista, Kitchen, SignalR, DataGen, PostgreSQL, and Docker Compose behavior as twelve independently buildable .NET 10 lesson commits.

**Architecture:** Start with the smallest HTTP vertical slice, introduce a framework-free domain, add application ports and PostgreSQL adapters, then add in-process dispatch and preparation modules. Keep all runtime modules in one host during Phase 1; Phase 2 will extract assembly boundaries without changing behavior.

**Tech Stack:** .NET 10 SDK, ASP.NET Core Minimal APIs, xUnit, WebApplicationFactory, EF Core 10 with compatible Npgsql provider, Testcontainers PostgreSQL, MediatR, FluentValidation, SignalR, TypeScript/Vite, and Docker Compose.

## Global Constraints

- Work only on the orphan branch `learning/dotnet10-rebuild`.
- Target `net10.0`, enable nullable reference types, implicit usings, analyzers, deterministic builds, and warnings-as-errors for repository-owned code.
- Keep each lesson commit green; never retain a deliberately failing test commit.
- Preserve original `/v1` request/response behavior during this phase.
- Use UTC through an injectable `TimeProvider`; do not call `DateTime.UtcNow` in domain/application code.
- Do not wait real preparation delays in tests.
- Do not commit credentials; Compose development credentials must be clearly local-only and overridable.
- Add/update the matching Vietnamese lesson document in the same commit.

---

### Task 1: Bootstrap a verifiable .NET 10 solution

**Files:**

- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `CoffeeShop.slnx`
- Create: `src/CoffeeShop.Api/CoffeeShop.Api.csproj`
- Create: `src/CoffeeShop.Api/Program.cs`
- Create: `tests/CoffeeShop.UnitTests/CoffeeShop.UnitTests.csproj`
- Create: `tests/CoffeeShop.UnitTests/BootstrapTests.cs`
- Create: `.github/workflows/ci.yml`
- Create: `LICENSE`
- Replace: `README.md`
- Create: `docs/lessons/01-bootstrap-dotnet-10.md`

**Interfaces:**

- Produces a `net10.0` web host with `public partial class Program` for later functional tests.
- Produces the common commands `dotnet build CoffeeShop.slnx` and `dotnet test CoffeeShop.slnx`.

- [ ] **Step 1: Create the orphan curriculum branch**

```bash
git switch --orphan learning/dotnet10-rebuild
```

Replace the inherited one-line README rather than deleting broad paths. Copy the original MIT license text and attribute `thangchung/coffeeshop-modular` in the new README.

- [ ] **Step 2: Scaffold solution and projects**

```bash
dotnet new globaljson --sdk-version 10.0.107 --roll-forward latestFeature
dotnet new sln --name CoffeeShop --format slnx
dotnet new web --name CoffeeShop.Api --output src/CoffeeShop.Api --framework net10.0
dotnet new xunit --name CoffeeShop.UnitTests --output tests/CoffeeShop.UnitTests --framework net10.0
dotnet sln CoffeeShop.slnx add src/CoffeeShop.Api/CoffeeShop.Api.csproj tests/CoffeeShop.UnitTests/CoffeeShop.UnitTests.csproj
```

- [ ] **Step 3: Establish shared build settings**

`Directory.Build.props` must contain:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AnalysisLevel>latest</AnalysisLevel>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

Keep generated test-package versions exact in `Directory.Packages.props`; enable central package management before adding later packages.

- [ ] **Step 4: Add a real bootstrap test**

```csharp
namespace CoffeeShop.UnitTests;

public sealed class BootstrapTests
{
    [Fact]
    public void Runtime_targets_dotnet_10()
    {
        Assert.StartsWith("10.", Environment.Version.ToString());
    }
}
```

- [ ] **Step 5: Add CI and lesson material**

CI checks out the repository, installs `10.0.x`, restores, builds with `--no-restore`, and tests with `--no-build`. The Vietnamese lesson explains SDK versus runtime, `global.json`, central package management, solution files, and the green-commit contract.

- [ ] **Step 6: Verify and commit**

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --no-restore
dotnet test CoffeeShop.slnx --no-build
git add .
git commit -m "lesson(01): bootstrap the .NET 10 solution"
```

---

### Task 2: Expose the original place-order HTTP contract

**Files:**

- Create: `src/CoffeeShop.Api/Features/Orders/PlaceOrder/PlaceOrderRequest.cs`
- Create: `src/CoffeeShop.Api/Features/Orders/PlaceOrder/PlaceOrderEndpoint.cs`
- Modify: `src/CoffeeShop.Api/Program.cs`
- Create: `tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj`
- Create: `tests/CoffeeShop.ApiTests/PlaceOrderEndpointTests.cs`
- Create: `client.http`
- Create: `docs/lessons/02-place-order-endpoint.md`

**Interfaces:**

- Consumes JSON fields `commandType`, `orderSource`, `location`, `loyaltyMemberId`, `baristaItems`, `kitchenItems`, and `timestamp`.
- Produces `POST /v1/api/orders` with the original empty `200 OK` success response.

- [ ] **Step 1: Add the API test project and failing functional test**

Add `Microsoft.AspNetCore.Mvc.Testing`, reference the API project, and write:

```csharp
[Fact]
public async Task Post_order_returns_ok_for_the_original_contract()
{
    using var client = _factory.CreateClient();
    using var response = await client.PostAsJsonAsync("/v1/api/orders", PlaceOrderSamples.Valid);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

Run the focused test and confirm it fails with 404.

- [ ] **Step 2: Implement the transport contract and endpoint**

```csharp
public sealed record PlaceOrderRequest(
    int CommandType,
    int OrderSource,
    int Location,
    Guid LoyaltyMemberId,
    IReadOnlyList<PlaceOrderItemRequest> BaristaItems,
    IReadOnlyList<PlaceOrderItemRequest> KitchenItems,
    DateTimeOffset Timestamp);

public sealed record PlaceOrderItemRequest(int ItemType);
```

Map the endpoint through a `MapPlaceOrder()` extension. At this lesson only, it validates JSON binding and returns `Results.Ok()`; the lesson explicitly labels missing domain behavior as debt for Lesson 03.

- [ ] **Step 3: Cover transport edge cases**

Add tests for malformed JSON returning 400 and numeric enums matching the original payload. Keep business validation out of this transport-only lesson.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test CoffeeShop.slnx
git add .
git commit -m "lesson(02): expose the place-order endpoint"
```

---

### Task 3: Model orders and menu pricing

**Files:**

- Create: `src/CoffeeShop.Domain/CoffeeShop.Domain.csproj`
- Create: `src/CoffeeShop.Domain/Orders/Order.cs`
- Create: `src/CoffeeShop.Domain/Orders/LineItem.cs`
- Create: `src/CoffeeShop.Domain/Orders/OrderStatus.cs`
- Create: `src/CoffeeShop.Domain/Orders/ItemStatus.cs`
- Create: `src/CoffeeShop.Domain/Orders/OrderSource.cs`
- Create: `src/CoffeeShop.Domain/Orders/Location.cs`
- Create: `src/CoffeeShop.Domain/Menu/ItemType.cs`
- Create: `src/CoffeeShop.Domain/Menu/MenuItem.cs`
- Create: `src/CoffeeShop.Domain/Menu/MenuCatalog.cs`
- Create: `src/CoffeeShop.Domain/Common/DomainException.cs`
- Create: `tests/CoffeeShop.DomainTests/CoffeeShop.DomainTests.csproj`
- Create: `tests/CoffeeShop.DomainTests/Orders/OrderTests.cs`
- Create: `tests/CoffeeShop.DomainTests/Menu/MenuCatalogTests.cs`
- Create: `docs/lessons/03-order-domain-model.md`

**Interfaces:**

- Produces `Order.Place(...)`, `Order.CompleteItem(...)`, and `MenuCatalog.Get(ItemType)`.
- The domain accepts typed item selections, computes server-owned names/prices, and rejects empty orders.

- [ ] **Step 1: Write domain tests first**

```csharp
[Fact]
public void Place_uses_catalog_price_instead_of_client_data()
{
    var order = Order.Place(OrderSource.Counter, Location.Atlanta, Guid.NewGuid(),
        [new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista)]);

    var line = Assert.Single(order.LineItems);
    Assert.Equal(4.50m, line.Price);
    Assert.Equal(ItemStatus.InProgress, line.Status);
}

[Fact]
public void Place_rejects_an_empty_order()
{
    Assert.Throws<DomainException>(() =>
        Order.Place(OrderSource.Counter, Location.Atlanta, Guid.NewGuid(), []));
}
```

Run and confirm compilation fails because the domain types do not exist.

- [ ] **Step 2: Implement the minimum framework-free domain**

Use private collections, read-only exposure, private constructors for persistence, and explicit factories. `MenuCatalog` must exhaustively map all ten original item types and throw for unknown values; it must not silently fall back to cappuccino.

- [ ] **Step 3: Map HTTP requests into the domain**

Reference Domain from API. Convert numeric request values with explicit `Enum.IsDefined` checks, combine Barista/Kitchen item requests with their station, call `Order.Place`, and keep the created order in a temporary singleton in-memory store until Lesson 04.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test CoffeeShop.slnx
git add .
git commit -m "lesson(03): model orders and menu pricing"
```

---

### Task 4: Persist orders with EF Core and PostgreSQL

**Files:**

- Create: `src/CoffeeShop.Application/CoffeeShop.Application.csproj`
- Create: `src/CoffeeShop.Application/Orders/IOrderRepository.cs`
- Create: `src/CoffeeShop.Infrastructure/CoffeeShop.Infrastructure.csproj`
- Create: `src/CoffeeShop.Infrastructure/Persistence/CoffeeShopDbContext.cs`
- Create: `src/CoffeeShop.Infrastructure/Persistence/Configurations/OrderConfiguration.cs`
- Create: `src/CoffeeShop.Infrastructure/Persistence/Configurations/LineItemConfiguration.cs`
- Create: `src/CoffeeShop.Infrastructure/Persistence/EfOrderRepository.cs`
- Create: `src/CoffeeShop.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`
- Create: `src/CoffeeShop.Infrastructure/DependencyInjection.cs`
- Create: `src/CoffeeShop.Infrastructure/Persistence/Migrations/*`
- Modify: `src/CoffeeShop.Api/Program.cs`
- Modify: `src/CoffeeShop.Api/Features/Orders/PlaceOrder/PlaceOrderEndpoint.cs`
- Create: `tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj`
- Create: `tests/CoffeeShop.IntegrationTests/PostgreSqlFixture.cs`
- Create: `tests/CoffeeShop.IntegrationTests/OrderPersistenceTests.cs`
- Create: `docs/lessons/04-ef-core-postgresql.md`

**Interfaces:**

```csharp
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);
    Task<Order?> FindAsync(Guid orderId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Add current compatible packages**

Use `dotnet add package` without prerelease flags for EF Core Design, Npgsql EF provider, and Testcontainers PostgreSQL. Pin the successfully restored exact versions centrally and verify EF Core/Npgsql major versions match.

- [ ] **Step 2: Write the failing PostgreSQL integration test**

```csharp
[Fact]
public async Task Saves_and_reloads_an_order_with_line_items()
{
    await using var db = _fixture.CreateDbContext();
    var order = OrderSamples.CappuccinoAndCroissant();
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    var reloaded = await db.Orders.Include(x => x.LineItems).SingleAsync(x => x.Id == order.Id);
    Assert.Equal(2, reloaded.LineItems.Count);
}
```

- [ ] **Step 3: Implement mappings and migration**

Use schemas `ordering`, `barista`, and `kitchen` consistently with the original model. Map IDs as UUID, money with explicit precision, enums as stable strings, and the Order-LineItem relationship with a private field or supported backing collection. Generate and inspect a named initial migration.

- [ ] **Step 4: Replace in-memory persistence**

Register the DbContext and repository, map configuration from `ConnectionStrings:CoffeeShop`, and save through `IOrderRepository`. Tests override the connection string through the factory; no password is committed to appsettings.

- [ ] **Step 5: Verify and commit**

```bash
dotnet test CoffeeShop.slnx
git add .
git commit -m "lesson(04): persist orders with EF Core and PostgreSQL"
```

---

### Task 5: Query fulfilled orders with specifications

**Files:**

- Create: `src/CoffeeShop.Application/Common/Queries/ISpecification.cs`
- Create: `src/CoffeeShop.Application/Orders/FulfilledOrdersSpecification.cs`
- Create: `src/CoffeeShop.Application/Orders/FulfilledOrderDto.cs`
- Extend: `src/CoffeeShop.Application/Orders/IOrderRepository.cs`
- Modify: `src/CoffeeShop.Infrastructure/Persistence/EfOrderRepository.cs`
- Create: `src/CoffeeShop.Api/Features/Orders/GetFulfilled/GetFulfilledOrdersEndpoint.cs`
- Create: `tests/CoffeeShop.ApiTests/GetFulfilledOrdersTests.cs`
- Create: `docs/lessons/05-query-specifications.md`

**Interfaces:**

```csharp
Task<IReadOnlyList<Order>> ListAsync(
    ISpecification<Order> specification,
    CancellationToken cancellationToken);
```

- [ ] **Step 1: Write failing functional and repository tests**

Seed one fulfilled and one in-progress order. Assert `GET /v1/api/fulfillment-orders` returns only the fulfilled order with all line items.

- [ ] **Step 2: Implement the smallest useful specification**

The abstraction contains only criteria and include expressions required by this repository. Do not port unused paging, grouping, predicate composition, or generic grid abstractions from the original library.

- [ ] **Step 3: Implement the read endpoint**

Use `AsNoTracking`, explicit DTO projection, cancellation, and deterministic ordering. Do not serialize EF entities directly.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test CoffeeShop.slnx
git add .
git commit -m "lesson(05): query fulfilled orders with specifications"
```

---

### Task 6: Dispatch use cases and validate requests

**Files:**

- Create: `src/CoffeeShop.Application/Orders/PlaceOrder/PlaceOrderCommand.cs`
- Create: `src/CoffeeShop.Application/Orders/PlaceOrder/PlaceOrderHandler.cs`
- Create: `src/CoffeeShop.Application/Orders/PlaceOrder/PlaceOrderValidator.cs`
- Create: `src/CoffeeShop.Application/Orders/GetFulfilled/GetFulfilledOrdersQuery.cs`
- Create: `src/CoffeeShop.Application/Orders/GetFulfilled/GetFulfilledOrdersHandler.cs`
- Create: `src/CoffeeShop.Application/Common/Behaviors/ValidationBehavior.cs`
- Create: `src/CoffeeShop.Application/DependencyInjection.cs`
- Modify: both order endpoints to call `ISender`
- Create: `tests/CoffeeShop.ApplicationTests/*`
- Create: `docs/lessons/06-mediatr-validation.md`

**Interfaces:**

- `PlaceOrderCommand : IRequest<PlaceOrderResult>`.
- `GetFulfilledOrdersQuery : IRequest<IReadOnlyList<FulfilledOrderDto>>`.
- `ValidationBehavior<TRequest,TResponse> : IPipelineBehavior<TRequest,TResponse>`.

- [ ] **Step 1: Add and pin current MediatR and FluentValidation packages**

Register logging before MediatR. Register handlers by application assembly and add the open validation behavior through the current configuration API. Document MediatR's current license-key behavior and keep the key external to Git.

- [ ] **Step 2: Write failing handler and validation tests**

Tests assert that the handler creates/persists an order and that empty items, undefined enums, and empty loyalty member IDs fail before persistence.

- [ ] **Step 3: Implement commands, handlers, validators, and behavior**

Keep ASP.NET types out of Application. Endpoints translate `PlaceOrderResult` to the original HTTP response.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test CoffeeShop.slnx
git add .
git commit -m "lesson(06): dispatch use cases and validate requests"
```

---

### Task 7: Raise and dispatch domain events

**Files:**

- Create: `src/CoffeeShop.Domain/Common/IDomainEvent.cs`
- Create: `src/CoffeeShop.Domain/Common/AggregateRoot.cs`
- Create: `src/CoffeeShop.Domain/Orders/Events/OrderItemAccepted.cs`
- Create: `src/CoffeeShop.Application/Common/Events/IDomainEventDispatcher.cs`
- Create: `src/CoffeeShop.Infrastructure/Events/MediatRDomainEventDispatcher.cs`
- Modify: `Order`, DbContext/repository save path, and DI
- Create: `tests/CoffeeShop.DomainTests/Orders/OrderEventTests.cs`
- Create: `tests/CoffeeShop.ApplicationTests/DomainEventDispatchTests.cs`
- Create: `docs/lessons/07-domain-events.md`

**Interfaces:**

```csharp
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing event tests**

Assert one accepted event per line item, correct station/item/order IDs, and no duplicate event after dispatch clears the collection.

- [ ] **Step 2: Implement aggregate event collection and dispatcher**

Domain event types do not implement MediatR interfaces. The infrastructure adapter wraps or dispatches them to registered application handlers. Save database state before dispatch to reproduce original behavior; document the dual-write gap reserved for the outbox phase.

- [ ] **Step 3: Verify and commit**

```bash
dotnet test CoffeeShop.slnx
git add .
git commit -m "lesson(07): dispatch in-process domain events"
```

---

### Task 8: Simulate Barista preparation

**Files:**

- Create: `src/CoffeeShop.Api/Modules/Barista/BaristaItem.cs`
- Create: `src/CoffeeShop.Api/Modules/Barista/BaristaPreparationPolicy.cs`
- Create: `src/CoffeeShop.Api/Modules/Barista/HandleBaristaOrderItemAccepted.cs`
- Extend: `CoffeeShopDbContext` and mappings/migration
- Create: `tests/CoffeeShop.ApplicationTests/BaristaPreparationTests.cs`
- Create: `docs/lessons/08-barista-preparation.md`

**Interfaces:**

- Consume `OrderItemAccepted` only when station is Barista.
- Use injected `TimeProvider` and `IPreparationDelay.DelayAsync(TimeSpan,CancellationToken)`.
- Produce an in-process `OrderItemPrepared` event.

- [ ] **Step 1: Write deterministic failing tests**

Use fake delay/time implementations. Assert original durations: black/room coffee 5 seconds, espresso variants 7 seconds, cappuccino 10 seconds, default beverage 3 seconds.

- [ ] **Step 2: Implement handler and persistence**

Honor cancellation, persist preparation timestamps, and publish the prepared event only after persistence succeeds.

- [ ] **Step 3: Verify and commit**

```bash
dotnet test CoffeeShop.slnx
git add .
git commit -m "lesson(08): process barista items asynchronously"
```

---

### Task 9: Complete Kitchen items and orders

**Files:**

- Create: `src/CoffeeShop.Api/Modules/Kitchen/KitchenItem.cs`
- Create: `src/CoffeeShop.Api/Modules/Kitchen/KitchenPreparationPolicy.cs`
- Create: `src/CoffeeShop.Api/Modules/Kitchen/HandleKitchenOrderItemAccepted.cs`
- Create: `src/CoffeeShop.Application/Orders/HandleOrderItemPrepared.cs`
- Modify: Order transition and persistence mappings/migration
- Create: `tests/CoffeeShop.DomainTests/Orders/OrderCompletionTests.cs`
- Create: `tests/CoffeeShop.ApplicationTests/KitchenPreparationTests.cs`
- Create: `docs/lessons/09-kitchen-order-completion.md`

**Interfaces:**

- Kitchen consumes accepted food items and produces `OrderItemPrepared`.
- Counter consumes `OrderItemPrepared`; `Order.CompleteItem(lineItemId)` returns whether state changed.

- [ ] **Step 1: Write state-transition tests**

Cover partial completion, all-items completion, duplicate event no-op, unknown line ID failure, and two completion events arriving in either order.

- [ ] **Step 2: Implement Kitchen timing and order completion**

Preserve original food timing while using deterministic delay. Add an optimistic concurrency token to Order so simultaneous completions cannot silently overwrite each other; handle the retry at the application boundary.

- [ ] **Step 3: Verify and commit**

```bash
dotnet test CoffeeShop.slnx
git add .
git commit -m "lesson(09): process kitchen items and fulfill orders"
```

---

### Task 10: Stream typed updates with SignalR

**Files:**

- Create: `src/CoffeeShop.Api/Realtime/OrderUpdateMessage.cs`
- Create: `src/CoffeeShop.Api/Realtime/IOrderUpdatesClient.cs`
- Create: `src/CoffeeShop.Api/Realtime/OrderUpdatesHub.cs`
- Create: `src/CoffeeShop.Api/Realtime/SignalROrderUpdatePublisher.cs`
- Modify: event handlers and `Program.cs`
- Create: `src/CoffeeShop.SignalRClient/package.json`
- Create: `src/CoffeeShop.SignalRClient/package-lock.json`
- Create: `src/CoffeeShop.SignalRClient/tsconfig.json`
- Create: `src/CoffeeShop.SignalRClient/index.html`
- Create: `src/CoffeeShop.SignalRClient/src/main.ts`
- Create: `src/CoffeeShop.SignalRClient/src/style.css`
- Create: `tests/CoffeeShop.ApiTests/OrderUpdateBroadcastTests.cs`
- Create: `docs/lessons/10-signalr-client.md`

**Interfaces:**

```csharp
public sealed record OrderUpdateMessage(
    Guid OrderId,
    Guid LineItemId,
    string ItemType,
    string ItemStatus,
    string OrderStatus,
    string? MadeBy,
    DateTimeOffset OccurredAt);
```

- [ ] **Step 1: Write failing broadcaster test**

Mock the typed hub context and assert accepted/prepared updates are mapped without concatenating business data into an opaque string.

- [ ] **Step 2: Implement typed hub and safe CORS configuration**

Map `/message`, accept an explicit configured client origin, allow credentials only for that origin, and remove any allow-all predicate.

- [ ] **Step 3: Build the vanilla TypeScript client**

Initialize Vite, add `@microsoft/signalr`, enable strict TypeScript, handle reconnecting/reconnected/closed states, render text with DOM APIs rather than `innerHTML`, and use configuration for the hub URL.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test CoffeeShop.slnx
npm ci --prefix src/CoffeeShop.SignalRClient
npm run build --prefix src/CoffeeShop.SignalRClient
git add .
git commit -m "lesson(10): stream order updates with SignalR"
```

---

### Task 11: Generate deterministic demo orders

**Files:**

- Create: `src/CoffeeShop.DataGen/CoffeeShop.DataGen.csproj`
- Create: `src/CoffeeShop.DataGen/Program.cs`
- Create: `src/CoffeeShop.DataGen/OrderGeneratorOptions.cs`
- Create: `src/CoffeeShop.DataGen/OrderGeneratorWorker.cs`
- Create: `src/CoffeeShop.DataGen/RandomOrderFactory.cs`
- Create: `tests/CoffeeShop.DataGenTests/CoffeeShop.DataGenTests.csproj`
- Create: `tests/CoffeeShop.DataGenTests/RandomOrderFactoryTests.cs`
- Create: `tests/CoffeeShop.DataGenTests/OrderGeneratorWorkerTests.cs`
- Create: `docs/lessons/11-data-generator.md`

**Interfaces:**

```csharp
public sealed class OrderGeneratorOptions
{
    public required Uri ApiBaseUrl { get; init; }
    public int OrderCount { get; init; } = 10;
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(1);
    public int Seed { get; init; } = 20260808;
}
```

- [ ] **Step 1: Write deterministic factory and worker tests**

Assert the same seed produces the same valid sequence, `OrderCount` limits calls, cancellation stops cleanly, and non-success HTTP status is logged/handled according to a bounded policy.

- [ ] **Step 2: Implement worker with HttpClientFactory and validated options**

Create `Random` once from the configured seed, use `PeriodicTimer` or injected delay, and never instantiate a new RNG for every order.

- [ ] **Step 3: Verify and commit**

```bash
dotnet test CoffeeShop.slnx
git add .
git commit -m "lesson(11): add a configurable order generator"
```

---

### Task 12: Run Phase 1 with Docker Compose

**Files:**

- Create: `.dockerignore`
- Create: `src/CoffeeShop.Api/Dockerfile`
- Create: `src/CoffeeShop.SignalRClient/Dockerfile`
- Create: `src/CoffeeShop.DataGen/Dockerfile`
- Create: `compose.yaml`
- Create: `scripts/phase-1-smoke.sh`
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`
- Create: `docs/lessons/12-docker-compose.md`

**Interfaces:**

- Services: `postgres`, `api`, `signalr-client`, and opt-in `datagen` profile.
- API health endpoints: `/health/live` and `/health/ready` with PostgreSQL readiness.
- Smoke script waits for readiness, places a deterministic order, polls fulfillment with a deadline, and exits nonzero on failure.

- [ ] **Step 1: Write/verify multi-stage images**

Build artifacts in SDK/Node stages and run them in non-root runtime/web-server stages. Use container DNS names, not localhost, between services.

- [ ] **Step 2: Add Compose and migration startup**

Use environment variables for connection strings and origins. Keep local-only default credentials explicit and overridable. Use health conditions where supported, but retain application retry/readiness behavior.

- [ ] **Step 3: Add the bounded smoke test**

The POSIX shell script uses `curl`, has a fixed timeout, prints relevant service logs on failure, and never loops forever.

- [ ] **Step 4: Run full Phase 1 verification**

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --no-restore
dotnet test CoffeeShop.slnx --no-build
npm ci --prefix src/CoffeeShop.SignalRClient
npm run build --prefix src/CoffeeShop.SignalRClient
docker compose build
docker compose up -d postgres api signalr-client
./scripts/phase-1-smoke.sh
docker compose down --volumes
```

- [ ] **Step 5: Commit and tag**

```bash
git add .
git commit -m "lesson(12): compose the original coffee shop flow"
git tag -a phase-1-original-behavior -m "Phase 1: original behavior on .NET 10"
```

## Phase 1 Self-Review and Handoff

- [ ] Confirm Lessons 01–12 exist in order and each has a matching Vietnamese document.
- [ ] Confirm no secret, build output, container volume, or developer-specific path is tracked.
- [ ] Run `git diff --check` and the complete verification sequence.
- [ ] Audit each lesson commit using the verification contract available at that commit.
- [ ] Write the Phase 2 detailed plan before implementing Lesson 13.
