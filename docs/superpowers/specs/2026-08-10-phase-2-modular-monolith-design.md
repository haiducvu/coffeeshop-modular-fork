# Phase 2 Modular Monolith Design

## 1. Purpose

Phase 2 turns the working Phase 1 process into a real modular monolith without breaking the original `/v1` order flow. It makes module ownership executable, adds a resource-oriented API, standard failures, external authentication, policy authorization, Redis-backed query caching, and operational foundations.

The phase remains a single deployable ASP.NET Core process. Kafka is deliberately deferred to Phase 3, after the in-process module seams are explicit and protected.

## 2. Constraints Carried Forward

- Target .NET 10 and keep centrally pinned package versions.
- Preserve `POST /v1/api/orders`, `GET /v1/api/fulfillment-orders`, and the SignalR client behavior.
- Keep code and commit messages in English; keep lesson documents in Vietnamese.
- Every lesson is one coherent commit that restores, builds, tests, and runs its applicable frontend/container checks independently.
- Push each lesson immediately after its green verification gate.
- Domain implementation cannot depend directly on ASP.NET Core, EF Core, Redis, JWT, Serilog, Kafka, MediatR, or Dapr.
- Do not implement a custom identity provider. Use a standards-based external provider for the real demo and deterministic test authentication for functional tests.
- Never treat a cache as command-side authority.
- No Kafka broker or integration-event implementation enters Phase 2.

## 3. Approaches Considered

### 3.1 Assembly rename only

Move current namespaces into Counter, Barista, and Kitchen projects while retaining one shared application project and one shared persistence project.

This minimizes moves, but module ownership remains conventional rather than executable. A shared persistence implementation can reach every aggregate, and the architecture tests would protect naming more than behavior. Rejected.

### 3.2 One deep assembly per vertical module — selected

Create one assembly for each business module. Each contains its internal domain, application, and persistence implementation behind a small composition interface. Modules share only a framework-free SharedKernel and explicit in-process Contracts. Each module owns a PostgreSQL schema, `DbContext`, migrations, and repository adapters.

This gives callers leverage through small interfaces, keeps change local to the owning module, and establishes seams that can later receive Kafka adapters without rewriting business rules.

### 3.3 Layered assemblies inside every module

Create separate Domain, Application, and Infrastructure projects for Counter, Barista, and Kitchen.

This offers maximal compile-time granularity but creates at least nine business projects before tests and hosts. For this curriculum size, the larger public project graph and repeated composition code obscure the module lesson. Rejected as premature.

## 4. Target Project Topology

```text
CoffeeShop.Api
├── CoffeeShop.Modules.Counter
├── CoffeeShop.Modules.Barista
├── CoffeeShop.Modules.Kitchen
├── CoffeeShop.Contracts
└── CoffeeShop.SharedKernel

CoffeeShop.Modules.Counter ─┬─► CoffeeShop.Contracts
                            └─► CoffeeShop.SharedKernel
CoffeeShop.Modules.Barista ─┬─► CoffeeShop.Contracts
                            └─► CoffeeShop.SharedKernel
CoffeeShop.Modules.Kitchen ─┬─► CoffeeShop.Contracts
                            └─► CoffeeShop.SharedKernel
CoffeeShop.Contracts ─────────► CoffeeShop.SharedKernel
```

No module references another module assembly. `CoffeeShop.Api` is the composition root and transport adapter; referencing all modules is its job.

The Phase 1 `CoffeeShop.Domain`, `CoffeeShop.Application`, and `CoffeeShop.Infrastructure` projects are removed after their owned files move. Keeping compatibility wrapper assemblies would make the old layered structure another public interface and weaken the lesson.

## 5. Module Interfaces and Ownership

### 5.1 SharedKernel

The SharedKernel contains only semantics with evidence of real reuse:

- `IDomainEvent`
- `AggregateRoot`
- `DomainException`
- `IDomainEventDispatcher`
- `IDomainEventHandler<TDomainEvent>`
- `IPreparationDelay`

It is framework-free. The domain-event dispatcher implementation and real delay adapter live in the API host because they are composition concerns.

### 5.2 Contracts

Contracts contains the in-process language shared by Counter, Barista, Kitchen, and realtime adapters:

- `ItemType`, `PreparationStation`, `OrderStatus`, and `ItemStatus`
- `OrderItemAccepted`
- `OrderItemPrepared`
- `OrderUpdated`

These are not Kafka integration events. Phase 3 introduces separately named, versioned broker contracts and maps to them explicitly.

### 5.3 Counter

Counter owns:

- Order, LineItem, MenuCatalog, prices, and order state transitions.
- Place-order and fulfilled-order use cases.
- Counter repositories, specifications, PostgreSQL mappings, schema, and migrations.
- Handling `OrderItemPrepared` to update the order.

The host uses two small interfaces:

```csharp
public interface ICounterModule
{
    Task<PlaceOrderResult> PlaceOrderAsync(
        PlaceOrderInput input,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FulfilledOrder>> GetFulfilledOrdersAsync(
        CancellationToken cancellationToken);
}

public static IServiceCollection AddCounterModule(
    this IServiceCollection services,
    string connectionString);

public static IServiceCollection AddCounterModuleForTesting(
    this IServiceCollection services);
```

Handlers, repositories, `DbContext`, and EF entities are implementation details. API transport DTOs map to module input/output records. The explicit testing registration uses an in-memory adapter so API tests do not need Docker; production cannot select it accidentally from configuration.

### 5.4 Barista and Kitchen

Barista owns drink preparation state, policy, repository, schema, migrations, and the handler for Barista `OrderItemAccepted` contracts. Kitchen owns the equivalent food behavior.

Their host interface is composition only:

```csharp
public static IServiceCollection AddBaristaModule(
    this IServiceCollection services,
    string connectionString);

public static IServiceCollection AddKitchenModule(
    this IServiceCollection services,
    string connectionString);
```

Runtime work enters through `IDomainEventHandler<OrderItemAccepted>` and exits through `OrderItemPrepared`. Neither module knows Counter's implementation.

## 6. Persistence

Modules share one PostgreSQL server and database but own independent schemas and migrations:

```text
counter.Orders / counter.LineItems
barista.Items
kitchen.Items
```

Each context uses a schema-local migrations history table. The API applies module migrations explicitly during startup using bounded Npgsql retries.

Lesson 13 is a phase-boundary persistence reset for the learning environment. The learner must run `docker compose down --volumes` before starting the new schema layout. This avoids disguising a complex production data migration as a module-refactor lesson. The observable HTTP and business behavior is preserved on a fresh database, and the limitation is documented. Subsequent Phase 2 migrations are incremental and module-owned.

## 7. In-Process Data Flow

```text
POST /v1/api/orders
  -> HTTP adapter
  -> ICounterModule.PlaceOrderAsync
  -> Counter aggregate + counter transaction
  -> OrderItemAccepted contract
  -> Barista/Kitchen domain-event handlers
  -> owned preparation transaction
  -> OrderItemPrepared contract
  -> Counter handler + optimistic concurrency
  -> OrderUpdated contract
  -> SignalR adapter
```

The framework-free domain-event interface is the in-process seam. The host dispatcher resolves typed handlers and invokes them sequentially, preserving the Phase 1 event ordering. Post-save dispatch retains the already documented transactional gap until Phase 3 introduces the Outbox.

## 8. Architecture Fitness Functions

Lesson 14 adds an architecture-test project that fails when:

- A module references another module assembly.
- Contracts references any module or host.
- SharedKernel references Contracts, modules, or host.
- A module's Domain namespace depends on EF Core, ASP.NET Core, MediatR, Redis, Serilog, or JWT assemblies.
- The API host bypasses the Counter interface and depends on Counter implementation namespaces.

The lesson includes a documented mutation command and expected failure so learners see the rule acting as an executable architecture decision.

## 9. HTTP Evolution and Failures

The original `/v1` routes remain unchanged. Lesson 15 adds:

```text
POST /v2/orders -> 201 Created
Location: /v2/orders/{orderId}
body: { orderId, status, links }

GET /v2/orders/{orderId}
```

Both versions call the same Counter interface. Versioning is path-based and explicit; no versioning package is added merely to route two versions.

Lesson 16 installs ASP.NET Core Problem Details and a single exception handler. It maps validation, not-found, optimistic concurrency, and unexpected failures to stable problem types. Unexpected responses never expose exception messages or stack traces; all problems include a trace identifier.

## 10. Authentication and Authorization

Lesson 17 configures JWT Bearer validation for tokens issued by a containerized Keycloak realm. The application validates issuer, audience, signature, lifetime, and HTTPS metadata according to environment. It never stores passwords or issues identities.

Functional tests replace JWT validation with a deterministic test authentication scheme. Compose adds Keycloak only under an `identity` profile and imports a versioned development realm without committed secrets.

Lesson 18 adds policies:

- `CoffeeShop.Customer`: create/read the caller's customer-facing orders.
- `CoffeeShop.FulfillmentReader`: read fulfillment queues.
- `CoffeeShop.Operator`: operational endpoints and privileged actions.

Endpoints declare policies centrally. Authorization matrix tests prove anonymous, allowed, and forbidden cases; production logic contains no scattered role-string checks.

## 11. Redis Read-Model Cache

Lesson 19 adds a cache-aside adapter only around fulfilled-order queries:

```text
ICounterModule query
  -> cache lookup
  -> miss: Counter query -> serialize -> bounded TTL
  -> hit: deserialize read model
OrderUpdated -> invalidate fulfillment cache
```

Commands and aggregates always use PostgreSQL. Cache keys are versioned, serialization failures degrade to a miss, and Redis unavailability follows an explicit bounded fallback policy. Metrics record hit, miss, and invalidation counts without high-cardinality labels. Unit tests use an in-memory cache adapter; a container integration test uses Redis.

## 12. Operational Foundations

Lesson 20 adds Serilog structured logging at the host, validates all deployment-critical options on startup, redacts sensitive headers/claims, and expands health semantics:

- Liveness answers only whether the process can execute.
- Readiness includes all module PostgreSQL contexts and enabled external dependencies.
- Disabled optional dependencies do not make readiness fail.
- Health responses remain safe for unauthenticated orchestrator probes.

The phase checkpoint runs solution tests, frontend build, container builds, authentication/Redis profiles where applicable, and a bounded end-to-end smoke test.

## 13. Testing Strategy

- Module interface tests exercise Counter through `ICounterModule` rather than internal handlers.
- Domain tests stay focused on invariants but move beside their owning module references.
- Contract tests protect shared event shape and enum values.
- Module integration tests use PostgreSQL Testcontainers and migrate each owned context.
- Architecture tests enforce project and namespace dependency rules.
- API functional tests cover `/v1`, `/v2`, Problem Details, authentication, and policy matrices.
- Redis unit tests use an in-memory adapter; Redis integration tests use a real container.
- Compose smoke checks health, client, SignalR/CORS, authenticated order flow, and fulfillment with a fixed deadline.

No test waits real preparation time. No test requires a live external identity provider unless it is explicitly the Compose demo check.

## 14. Lesson Sequence

1. Lesson 13 — separate modules and owned persistence.
2. Lesson 14 — enforce dependency rules.
3. Lesson 15 — add resource-oriented `/v2` orders.
4. Lesson 16 — standardize failures with Problem Details.
5. Lesson 17 — authenticate with external JWT issuer.
6. Lesson 18 — authorize operations with policies.
7. Lesson 19 — cache fulfillment read models with Redis.
8. Lesson 20 — structured logs, validated options, health semantics, and checkpoint.

Each lesson removes one deliberate limitation and leaves the next lesson's concern visible.

## 15. Phase Exit Criteria

- Lessons 13–20 exist as eight ordered green commits with Vietnamese lesson documents and complete commit bodies.
- Counter, Barista, and Kitchen have no implementation references to one another.
- Architecture tests make dependency erosion fail CI.
- `/v1` remains compatible and `/v2` has resource semantics.
- Problem Details, JWT authentication, policies, Redis caching, structured logs, and health semantics are verified.
- The full Compose flow passes with bounded diagnostics.
- The history audit passes for every Phase 2 lesson.
- Annotated tag `phase-2-modular-monolith` points to Lesson 20 and is pushed without rewriting prior history.
