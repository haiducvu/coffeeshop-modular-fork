# Phase 2 Modular Monolith Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver Lessons 13–20 as eight independently green commits that turn the Phase 1 process into an enforced, secure, cached, observable modular monolith.

**Architecture:** One deep assembly owns each vertical business module, including its domain, use cases, and persistence implementation. The ASP.NET Core host composes modules through small public interfaces; modules communicate only through framework-free SharedKernel abstractions and explicit in-process Contracts.

**Tech Stack:** .NET 10.0.201 SDK, ASP.NET Core 10, EF Core 10.0.10, Npgsql 10.0.3, MediatR 14.2.0, FluentValidation 12.1.1, PostgreSQL 17, ArchUnitNET 0.13.3, Keycloak 26.5.2, JWT Bearer 10.0.10, Redis 8, Microsoft Redis cache 10.0.10, Testcontainers.Redis 4.13.0, Serilog.AspNetCore 10.0.0, Serilog.Sinks.Console 6.1.1, Node 22, Vite 8.2.1, TypeScript 7.0.2.

## Global Constraints

- Target `net10.0`; keep `TreatWarningsAsErrors`, nullable analysis, deterministic builds, and central package management enabled.
- Preserve unauthenticated Phase 1 behavior at `POST /v1/api/orders` and `GET /v1/api/fulfillment-orders`; new semantics use `/v2`.
- Keep SignalR path `/message`, DataGen behavior, and the bounded Phase 1 smoke flow working unless a lesson explicitly extends the smoke contract.
- Domain namespaces cannot depend on ASP.NET Core, EF Core, MediatR, Redis, JWT, Serilog, Kafka, or Dapr.
- Counter, Barista, and Kitchen cannot reference one another's assemblies.
- SharedKernel stays framework-free; Contracts contains in-process contracts, never Kafka integration events.
- Tests cannot sleep real preparation durations or require a live identity provider except an explicit Compose demo.
- Every task follows red → green → refactor, updates a Vietnamese lesson document and README, runs the complete gate, commits with Purpose/Verification/Knowledge summary, and pushes immediately.
- Do not implement Kafka in Phase 2.

---

### Task 13: Separate the coffee shop modules

**Commit:** `lesson(13): separate the coffee shop modules`

**Files:**

- Create: `src/CoffeeShop.SharedKernel/CoffeeShop.SharedKernel.csproj`
- Create: `src/CoffeeShop.SharedKernel/Domain/AggregateRoot.cs`
- Create: `src/CoffeeShop.SharedKernel/Domain/DomainException.cs`
- Create: `src/CoffeeShop.SharedKernel/Events/IDomainEvent.cs`
- Create: `src/CoffeeShop.SharedKernel/Events/IDomainEventDispatcher.cs`
- Create: `src/CoffeeShop.SharedKernel/Events/IDomainEventHandler.cs`
- Create: `src/CoffeeShop.SharedKernel/Time/IPreparationDelay.cs`
- Create: `src/CoffeeShop.Contracts/CoffeeShop.Contracts.csproj`
- Create: `src/CoffeeShop.Contracts/Menu/ItemType.cs`
- Create: `src/CoffeeShop.Contracts/Orders/OrderStatus.cs`
- Create: `src/CoffeeShop.Contracts/Orders/OrderItemAccepted.cs`
- Create: `src/CoffeeShop.Contracts/Orders/OrderItemPrepared.cs`
- Create: `src/CoffeeShop.Contracts/Orders/OrderUpdated.cs`
- Create: `src/CoffeeShop.Modules.Counter/CoffeeShop.Modules.Counter.csproj`
- Create: `src/CoffeeShop.Modules.Counter/CounterModule.cs`
- Create: `src/CoffeeShop.Modules.Counter/CounterModuleServiceCollectionExtensions.cs`
- Move Counter domain/application files from `src/CoffeeShop.Domain/Menu`, `src/CoffeeShop.Domain/Orders`, and `src/CoffeeShop.Application/Orders` into `src/CoffeeShop.Modules.Counter/Domain` and `Application`
- Move Counter persistence files into `src/CoffeeShop.Modules.Counter/Infrastructure/Persistence`
- Create: `src/CoffeeShop.Modules.Barista/CoffeeShop.Modules.Barista.csproj`
- Create: `src/CoffeeShop.Modules.Barista/BaristaModuleServiceCollectionExtensions.cs`
- Move Barista domain/application/persistence files into `src/CoffeeShop.Modules.Barista/{Domain,Application,Infrastructure}`
- Create: `src/CoffeeShop.Modules.Kitchen/CoffeeShop.Modules.Kitchen.csproj`
- Create: `src/CoffeeShop.Modules.Kitchen/KitchenModuleServiceCollectionExtensions.cs`
- Move Kitchen domain/application/persistence files into `src/CoffeeShop.Modules.Kitchen/{Domain,Application,Infrastructure}`
- Create: `src/CoffeeShop.Api/Events/ServiceProviderDomainEventDispatcher.cs`
- Create: `src/CoffeeShop.Api/Time/TaskPreparationDelay.cs`
- Modify: `src/CoffeeShop.Api/Program.cs`
- Modify: `src/CoffeeShop.Api/CoffeeShop.Api.csproj`
- Modify: `src/CoffeeShop.Api/Health/PostgreSqlReadinessHealthCheck.cs`
- Modify: `src/CoffeeShop.Api/Dockerfile`
- Delete: `src/CoffeeShop.Domain`
- Delete: `src/CoffeeShop.Application`
- Delete: `src/CoffeeShop.Infrastructure`
- Modify: `CoffeeShop.slnx`
- Modify: `tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj`
- Modify: `tests/CoffeeShop.ApplicationTests/CoffeeShop.ApplicationTests.csproj`
- Modify: `tests/CoffeeShop.DomainTests/CoffeeShop.DomainTests.csproj`
- Modify: `tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj`
- Modify: `tests/CoffeeShop.UnitTests/CoffeeShop.UnitTests.csproj`
- Modify: namespaces/usings in every existing `.cs` test file that references the removed layered assemblies
- Create: `tests/CoffeeShop.ApplicationTests/CounterModuleTests.cs`
- Create: `tests/CoffeeShop.IntegrationTests/ModuleSchemaTests.cs`
- Modify: `tests/CoffeeShop.IntegrationTests/PostgreSqlFixture.cs`
- Create: `docs/lessons/13-module-assemblies.md`
- Modify: `README.md`

**Interfaces:**

```csharp
public interface ICounterModule
{
    Task<PlaceOrderResult> PlaceOrderAsync(
        PlaceOrderInput input,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FulfilledOrder>> GetFulfilledOrdersAsync(
        CancellationToken cancellationToken);
}

public sealed record PlaceOrderInput(
    int OrderSource,
    int Location,
    Guid LoyaltyMemberId,
    IReadOnlyList<int> BaristaItems,
    IReadOnlyList<int> KitchenItems);

public sealed record PlaceOrderResult(Guid OrderId);

public interface IDomainEventHandler<in TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    Task HandleAsync(TDomainEvent domainEvent, CancellationToken cancellationToken);
}
```

Module composition produces `AddCounterModule(connectionString)`, `AddCounterModuleForTesting()`, `AddBaristaModule(connectionString)`, `AddKitchenModule(connectionString)`, and one `Migrate*ModuleAsync(IServiceProvider)` extension per production module.

- [ ] **Step 1: Add failing seam tests**

Write `CounterModuleTests` that resolves `ICounterModule` through `AddCounterModuleForTesting`, places a valid order, and receives a non-empty order ID. Write `ModuleSchemaTests` that migrates the three module contexts and asserts PostgreSQL contains schemas `counter`, `barista`, and `kitchen` with only their owned tables.

- [ ] **Step 2: Verify RED**

Run:

```bash
dotnet test tests/CoffeeShop.ApplicationTests/CoffeeShop.ApplicationTests.csproj \
  --filter FullyQualifiedName~CounterModuleTests
dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj \
  --filter FullyQualifiedName~ModuleSchemaTests
```

Expected: compile failures because the module projects and public interfaces do not exist.

- [ ] **Step 3: Create SharedKernel and Contracts**

Move only the approved shared semantics. Replace MediatR notification wrappers with the typed `IDomainEventHandler<T>` seam. Keep event payload enum values byte-for-byte compatible with Phase 1 JSON and SignalR mappings.

- [ ] **Step 4: Build the Counter deep module**

Move Order/Menu code, internal handlers, validation behavior, repositories, specifications, EF mappings, and an internal `CounterDbContext`. `CounterModule` is the only use-case facade. Production and testing registrations select different internal repository adapters explicitly.

Use schema-local migration history:

```csharp
options.UseNpgsql(connectionString, npgsql =>
{
    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "counter");
    npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
});
```

- [ ] **Step 5: Build Barista and Kitchen deep modules**

Move owned aggregates, preparation policies, event handlers, repositories, mappings, contexts, and migrations. Each handler consumes `OrderItemAccepted`, ignores events for the other station, and publishes `OrderItemPrepared` only after its owned transaction succeeds.

- [ ] **Step 6: Compose modules in the host**

Register the framework adapter `ServiceProviderDomainEventDispatcher`, real delay, module registrations, realtime event handlers, module migrations, and PostgreSQL readiness without exposing internal contexts to the host. Update Docker publish inputs for the five new projects.

- [ ] **Step 7: Adapt existing tests at the new seams**

Keep domain invariants and `/v1` contract assertions. Replace tests of the removed MediatR event wrapper with typed dispatcher tests. Replace direct shared `CoffeeShopDbContext` usage with the owning module test helper/context exposed through `InternalsVisibleTo` only to integration tests.

- [ ] **Step 8: Generate module-owned initial migrations**

Run one `dotnet ef migrations add Initial<Module>` command per context with its module project and API startup project. Inspect generated schemas, foreign keys, concurrency token, and history-table configuration. Document the intentional Phase 2 volume reset.

- [ ] **Step 9: Verify GREEN and behavior compatibility**

Run the complete .NET/frontend/container gate and `./scripts/phase-1-smoke.sh` on a fresh Compose volume. Expected: 0 warnings, all tests pass, client builds, images build, and an order fulfills.

- [ ] **Step 10: Document, commit, and push**

Explain deep modules, interface versus implementation, shared semantics, schema ownership, and the volume reset in `docs/lessons/13-module-assemblies.md`. Commit with the exact subject and push `learning/dotnet10-rebuild`.

---

### Task 14: Enforce module dependency rules

**Commit:** `lesson(14): enforce module dependency rules`

**Files:**

- Modify: `Directory.Packages.props`
- Create: `tests/CoffeeShop.ArchitectureTests/CoffeeShop.ArchitectureTests.csproj`
- Create: `tests/CoffeeShop.ArchitectureTests/ModuleDependencyTests.cs`
- Create: `tests/CoffeeShop.ArchitectureTests/DomainPurityTests.cs`
- Create: `tests/CoffeeShop.ArchitectureTests/PublicSurfaceTests.cs`
- Create: `docs/architecture/module-rules.md`
- Create: `docs/lessons/14-architecture-tests.md`
- Modify: `CoffeeShop.slnx`
- Modify: `README.md`

**Packages:** `TngTech.ArchUnitNET` 0.13.3 and `TngTech.ArchUnitNET.xUnit` 0.13.3.

**Rules:**

```csharp
Types().That().ResideInAssembly("CoffeeShop.Modules.Counter")
    .Should().NotDependOnAny(baristaTypes);

Types().That().ResideInAssembly("CoffeeShop.Modules.Counter")
    .Should().NotDependOnAny(kitchenTypes);

Types().That().ResideInNamespaceMatching(".*\\.Domain(\\..*)?")
    .Should().NotDependOnAny(forbiddenFrameworkTypes);
```

- [ ] **Step 1: Write a deliberately failing rule**

Add a temporary fixture type in the architecture-test assembly that models Counter depending on Barista, then assert the reusable rule reports its full type name and reason. This proves the fitness function itself can fail, not only that the current graph happens to pass.

- [ ] **Step 2: Run RED, then remove only the mutation fixture**

Run the focused test and capture the expected ArchUnitNET violation. Remove the mutation type while retaining the production rules and a documented copy/paste mutation in `docs/architecture/module-rules.md`.

- [ ] **Step 3: Implement project, namespace, framework, and public-surface rules**

Load all five assemblies once. Assert module isolation, SharedKernel/Contracts direction, Domain purity, and that the host references only public Counter interface namespaces rather than `.Internal` namespaces.

- [ ] **Step 4: Verify and commit**

Run the full gate, update Lesson 14 and README, commit with the exact subject/body contract, and push immediately.

---

### Task 15: Add a resource-oriented order API

**Commit:** `lesson(15): add a resource-oriented order API`

**Files:**

- Modify: `src/CoffeeShop.Modules.Counter/CounterModule.cs`
- Create: `src/CoffeeShop.Modules.Counter/Contracts/OrderDetails.cs`
- Create: `src/CoffeeShop.Modules.Counter/Application/GetOrder/GetOrderHandler.cs`
- Create: `src/CoffeeShop.Api/Features/Orders/V2/CreateOrderEndpoint.cs`
- Create: `src/CoffeeShop.Api/Features/Orders/V2/GetOrderEndpoint.cs`
- Create: `src/CoffeeShop.Api/Features/Orders/V2/CreateOrderRequest.cs`
- Create: `src/CoffeeShop.Api/Features/Orders/V2/OrderResourceResponse.cs`
- Create: `tests/CoffeeShop.ApiTests/V2OrderContractTests.cs`
- Create: `tests/CoffeeShop.ApplicationTests/GetOrderModuleTests.cs`
- Create: `docs/lessons/15-resource-oriented-api.md`
- Modify: `README.md`

**Interface addition:**

```csharp
Task<OrderDetails?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);
```

`POST /v2/orders` returns `201 Created`, `Location: /v2/orders/{id}`, and:

```json
{
  "orderId": "00000000-0000-0000-0000-000000000000",
  "status": "InProgress",
  "links": { "self": "/v2/orders/00000000-0000-0000-0000-000000000000" }
}
```

- [ ] **Step 1: Write failing `/v2` contract tests**

Assert status, `Location`, response body, subsequent GET, missing-order `404`, and unchanged `/v1` `200 OK` behavior.

- [ ] **Step 2: Verify RED**

Run `V2OrderContractTests`; expect `404` because `/v2/orders` is not mapped.

- [ ] **Step 3: Extend the Counter interface and implement thin HTTP adapters**

Return module-owned read records, map them to transport resources, and keep path versioning explicit without adding an API-versioning package.

- [ ] **Step 4: Verify and commit**

Run focused tests, the full gate, smoke `/v1`, update Lesson 15/README, commit, and push.

---

### Task 16: Standardize API failures with Problem Details

**Commit:** `lesson(16): standardize API failures with Problem Details`

**Files:**

- Create: `src/CoffeeShop.Api/Errors/CoffeeShopExceptionHandler.cs`
- Create: `src/CoffeeShop.Api/Errors/ProblemTypes.cs`
- Create: `src/CoffeeShop.Api/Errors/ValidationProblemFactory.cs`
- Modify: `src/CoffeeShop.Api/Program.cs`
- Modify: v2 endpoints to let mapped exceptions reach the handler
- Create: `tests/CoffeeShop.ApiTests/ProblemDetailsTests.cs`
- Create: `tests/CoffeeShop.ApiTests/UnexpectedFailureTests.cs`
- Create: `docs/lessons/16-problem-details.md`
- Modify: `README.md`

**Failure taxonomy:**

```text
FluentValidation.ValidationException -> 400 /problems/validation
OrderNotFoundException              -> 404 /problems/order-not-found
OrderConcurrencyException           -> 409 /problems/order-conflict
unexpected Exception                -> 500 /problems/internal
```

Every response contains `type`, `title`, `status`, `traceId`; validation adds a deterministic `errors` object. A `500` never includes exception type, message, stack trace, connection string, token, or payload.

- [ ] **Step 1: Write failing Problem Details tests**

Use the test host to trigger all four classes. Assert exact content type `application/problem+json`, stable problem types/titles, trace ID, validation keys, and safe unexpected details.

- [ ] **Step 2: Verify RED**

Expected: current v2 responses have missing/inconsistent bodies and unexpected failures escape the standardized contract.

- [ ] **Step 3: Implement one ASP.NET Core `IExceptionHandler`**

Register `AddProblemDetails`, `AddExceptionHandler<CoffeeShopExceptionHandler>`, and `UseExceptionHandler`. Log unexpected exceptions with trace ID and return safe Problem Details.

- [ ] **Step 4: Verify and commit**

Run all gates, document the error taxonomy and why `/v1` remains compatible, commit, and push.

---

### Task 17: Authenticate API clients with JWT Bearer

**Commit:** `lesson(17): authenticate API clients with JWT bearer`

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `src/CoffeeShop.Api/CoffeeShop.Api.csproj`
- Create: `src/CoffeeShop.Api/Authentication/JwtAuthenticationOptions.cs`
- Create: `src/CoffeeShop.Api/Authentication/AuthenticationExtensions.cs`
- Create: `tests/CoffeeShop.ApiTests/Authentication/TestAuthenticationHandler.cs`
- Modify: `tests/CoffeeShop.ApiTests/CoffeeShopApiFactory.cs`
- Create: `tests/CoffeeShop.ApiTests/AuthenticationTests.cs`
- Create: `deploy/keycloak/coffeeshop-realm.json`
- Modify: `compose.yaml`
- Create: `scripts/phase-2-identity-smoke.sh`
- Create: `docs/lessons/17-jwt-authentication.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`

**Packages/images:** `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.10; `quay.io/keycloak/keycloak:26.5.2`.

**Validated options:**

```csharp
public sealed class JwtAuthenticationOptions
{
    public const string SectionName = "Authentication";
    public bool Enabled { get; init; }
    public required string Authority { get; init; }
    public required string Audience { get; init; }
    public bool RequireHttpsMetadata { get; init; } = true;
}
```

- [ ] **Step 1: Write failing authentication tests**

Assert an anonymous request has no authenticated principal, a deterministic test ticket exposes `sub`, `scope`, and roles, expired/invalid real JWT fixtures are rejected by the configured bearer handler, and disabled authentication does not silently register a fake production identity.

- [ ] **Step 2: Verify RED**

Expected: no bearer scheme or validated authentication options exist.

- [ ] **Step 3: Implement optional production JWT validation and deterministic test authentication**

When enabled, validate issuer/authority, audience, signature, and lifetime. The test factory replaces only the authentication scheme; it does not bypass later authorization policies.

- [ ] **Step 4: Add Keycloak identity profile and bounded smoke**

Mount `coffeeshop-realm.json` read-only at `/opt/keycloak/data/import`, run `start-dev --import-realm`, bind its host port to loopback, and use non-secret development users. The script waits for realm discovery, requests a token from `/realms/coffeeshop/protocol/openid-connect/token`, calls an authenticated diagnostic endpoint, and has one global deadline.

- [ ] **Step 5: Verify and commit**

Run unit/functional gates without Keycloak, then Compose identity smoke with Keycloak, document threat boundaries and local-only credentials, commit, and push.

---

### Task 18: Authorize coffee shop operations with policies

**Commit:** `lesson(18): authorize operations with policies`

**Files:**

- Create: `src/CoffeeShop.Api/Authorization/CoffeeShopPolicies.cs`
- Create: `src/CoffeeShop.Api/Authorization/OrderOwnerRequirement.cs`
- Create: `src/CoffeeShop.Api/Authorization/OrderOwnerAuthorizationHandler.cs`
- Modify: v2 create/get endpoints
- Create: `src/CoffeeShop.Api/Features/Fulfillment/V2/GetFulfillmentOrdersEndpoint.cs`
- Create: `src/CoffeeShop.Api/Features/Operations/V2/GetOrderEndpoint.cs`
- Create: `tests/CoffeeShop.ApiTests/AuthorizationMatrixTests.cs`
- Modify: `deploy/keycloak/coffeeshop-realm.json`
- Modify: `scripts/phase-2-identity-smoke.sh`
- Create: `docs/lessons/18-policy-authorization.md`
- Modify: `README.md`

**Policies:**

```csharp
public const string Customer = "CoffeeShop.Customer";
public const string FulfillmentReader = "CoffeeShop.FulfillmentReader";
public const string Operator = "CoffeeShop.Operator";
public const string OrderOwner = "CoffeeShop.OrderOwner";
```

`/v1` remains anonymous for compatibility. `/v2/orders` requires Customer; customer GET uses resource ownership; `/v2/fulfillment-orders` requires FulfillmentReader or Operator; `/v2/operations/orders/{id}` requires Operator.

- [ ] **Step 1: Write a failing authorization matrix**

Cover anonymous `401`, authenticated wrong-role `403`, correct-role success, customer ownership success/failure, operator override, and unchanged anonymous `/v1` success.

- [ ] **Step 2: Verify RED**

Expected: all v2 identities currently receive the same result because policies are absent.

- [ ] **Step 3: Implement named and resource-based policies**

Map Keycloak realm roles once during token validation. Keep role strings in one class. Use `IAuthorizationService.AuthorizeAsync(user, order, OrderOwner)` after loading the read resource; never place role checks inside Counter.

- [ ] **Step 4: Verify and commit**

Run matrix tests, full gate, real-token identity smoke for each role, update docs/README, commit, and push.

---

### Task 19: Cache fulfillment read models with Redis

**Commit:** `lesson(19): cache fulfillment read models with Redis`

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `src/CoffeeShop.Modules.Counter/CoffeeShop.Modules.Counter.csproj`
- Create: `src/CoffeeShop.Modules.Counter/Application/Fulfillment/IFulfillmentOrdersCache.cs`
- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Caching/RedisFulfillmentOrdersCache.cs`
- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Caching/FulfillmentCacheOptions.cs`
- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Caching/FulfillmentCacheMetrics.cs`
- Create: `src/CoffeeShop.Modules.Counter/Application/Fulfillment/InvalidateFulfillmentCache.cs`
- Modify: `src/CoffeeShop.Modules.Counter/CounterModule.cs`
- Create: `tests/CoffeeShop.ApplicationTests/FulfillmentCacheTests.cs`
- Create: `tests/CoffeeShop.IntegrationTests/RedisFulfillmentCacheTests.cs`
- Modify: `tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj`
- Modify: `compose.yaml`
- Modify: `scripts/phase-1-smoke.sh`
- Create: `docs/lessons/19-redis-read-model-cache.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`

**Packages/images:** `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.10, `Testcontainers.Redis` 4.13.0, `redis:8-alpine`.

**Cache seam:**

```csharp
internal interface IFulfillmentOrdersCache
{
    Task<IReadOnlyList<FulfilledOrder>?> GetAsync(CancellationToken cancellationToken);
    Task SetAsync(IReadOnlyList<FulfilledOrder> orders, CancellationToken cancellationToken);
    Task RemoveAsync(CancellationToken cancellationToken);
}
```

Use key `fulfilled-orders:v1`; validate TTL in `[5 seconds, 1 hour]`. Cache exceptions log a warning and degrade to PostgreSQL. `OrderUpdated` invalidates after the Counter transaction reaches Fulfilled. Counters `coffeeshop.fulfillment.cache.hit`, `.miss`, and `.invalidation` have no customer/order labels.

- [ ] **Step 1: Write failing cache behavior tests**

Assert hit bypasses repository, miss queries and populates, malformed data becomes a miss, Redis failure falls back, a fulfilled event invalidates, and commands never read/write cache.

- [ ] **Step 2: Verify RED**

Expected: Counter always queries PostgreSQL and no cache seam/metrics exist.

- [ ] **Step 3: Implement cache-aside and invalidation**

Keep the adapter internal to Counter. Serialize only module read records with explicit JSON options. Use one shared connection and bounded command timeouts.

- [ ] **Step 4: Add real Redis integration proof**

Use `RedisBuilder("redis:8-alpine")`, verify set/get/remove and TTL, then add Redis to Compose with loopback-only host port and health-conditioned API startup when caching is enabled.

- [ ] **Step 5: Verify and commit**

Run full tests, frontend, images, Redis-enabled smoke, update CI/docs/README, commit, and push.

---

### Task 20: Add structured logs and operational health

**Commit:** `lesson(20): add structured logs and health checks`

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `src/CoffeeShop.Api/CoffeeShop.Api.csproj`
- Create: `src/CoffeeShop.Api/Configuration/CoffeeShopHostOptions.cs`
- Create: `src/CoffeeShop.Api/Configuration/ConfigurationExtensions.cs`
- Create: `src/CoffeeShop.Api/Logging/SensitiveDataDestructuringPolicy.cs`
- Create: `src/CoffeeShop.Api/Health/RedisReadinessHealthCheck.cs`
- Create: `src/CoffeeShop.Api/Health/IdentityProviderReadinessHealthCheck.cs`
- Modify: `src/CoffeeShop.Api/Health/PostgreSqlReadinessHealthCheck.cs`
- Modify: `src/CoffeeShop.Api/Program.cs`
- Modify: `src/CoffeeShop.Api/appsettings.json`
- Create: `tests/CoffeeShop.ApiTests/ConfigurationValidationTests.cs`
- Create: `tests/CoffeeShop.ApiTests/HealthSemanticsTests.cs`
- Create: `tests/CoffeeShop.ApiTests/StructuredLoggingTests.cs`
- Create: `scripts/phase-2-smoke.sh`
- Modify: `compose.yaml`
- Modify: `.github/workflows/ci.yml`
- Create: `docs/lessons/20-operational-foundations.md`
- Create: `docs/checkpoints/phase-2.md`
- Modify: `README.md`

**Packages:** `Serilog.AspNetCore` 10.0.0 and `Serilog.Sinks.Console` 6.1.1.

**Operational contract:**

```text
/health/live  -> process only
/health/ready -> PostgreSQL + enabled Redis + enabled identity discovery
```

All critical settings validate on start. JSON logs include timestamp, level, message, trace ID, request path, and status without Authorization headers, tokens, passwords, connection-string credentials, or complete order payloads.

- [ ] **Step 1: Write failing configuration, logging, and health tests**

Assert missing/invalid enabled dependency configuration fails startup with the option name; liveness stays healthy when a fake dependency fails; readiness becomes `503`; disabled dependencies are excluded; captured JSON logs contain correlation fields and redact sensitive values.

- [ ] **Step 2: Verify RED**

Expected: current logging is unstructured, dependency options are partially validated, and readiness cannot distinguish enabled Redis/identity failures.

- [ ] **Step 3: Configure Serilog and validated options**

Bootstrap logging before host build, read final settings from configuration, enrich from log context, use request logging, and filter/redact sensitive properties. Keep framework logging abstractions inside modules.

- [ ] **Step 4: Implement explicit health semantics**

Register tagged checks conditionally from validated options. PostgreSQL uses bounded connection checks, Redis uses the shared multiplexer `PingAsync`, and identity uses a named HttpClient with a short timeout against OIDC discovery. Health response bodies expose names/status/duration only.

- [ ] **Step 5: Run the Phase 2 checkpoint**

Run restore/build/test, frontend build, all images, core Compose, Keycloak identity smoke, Redis cache proof, and `scripts/phase-2-smoke.sh`. Audit Lessons 13–20 in fresh worktrees using the verification contract at each commit.

- [ ] **Step 6: Review, document, commit, push, and tag**

Request code review and fix all Critical/Important findings. Complete Lesson 20 and checkpoint docs, commit/push, then create annotated tag `phase-2-modular-monolith` only after the eight-commit history audit passes.

## Plan Self-Review

- Spec coverage: Tasks 13–20 cover every design section and master-roadmap deliverable.
- Completeness scan: every step names its concrete interface, behavior, failure policy, and verification command.
- Type consistency: `ICounterModule`, `PlaceOrderInput`, `PlaceOrderResult`, `OrderDetails`, domain-event interfaces, policy names, cache seam, and option names are consistent across dependent tasks.
- Scope: Kafka, Outbox, inbox, DLT, Avro, OpenTelemetry, Dapr, and service extraction remain outside Phase 2.
