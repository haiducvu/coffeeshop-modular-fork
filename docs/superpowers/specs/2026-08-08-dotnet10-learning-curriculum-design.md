# CoffeeShop .NET 10 Learning Curriculum Design

## 1. Purpose

This repository will become a Vietnamese, commit-by-commit learning curriculum that reconstructs the behavior of the original `thangchung/coffeeshop-modular` project on .NET 10 and then improves it into a production-minded modular monolith and event-driven capstone.

The curriculum must let a learner check out any lesson commit, build it, run its applicable tests, understand why the code exists, and see which deliberate limitation is removed by the next lesson.

## 2. Source and Attribution

- Original project: `https://github.com/thangchung/coffeeshop-modular`.
- Original local reference: `/Users/phuongtrinh/haivu/03_dontnet-2026/coffeeshop-modular`.
- Destination repository: `https://github.com/haiducvu/coffeeshop-modular-fork.git`.
- Destination workspace: `/Users/phuongtrinh/haivu/dontnet-2026/coffeeshop-modular-fork`.
- Preserve the original license and clearly identify original behavior versus educational modernization.
- Preserve all 15 commits from the original .NET 7 repository on `original/dotnet7`. Build the curriculum on the orphan branch `learning/dotnet10-rebuild` without rewriting that reference branch.

## 3. Outcomes

By completing the curriculum, the learner should be able to explain and implement:

- ASP.NET Core Minimal APIs on .NET 10.
- Vertical slices and CQRS-style request handling.
- Domain entities, aggregate roots, invariants, and domain events.
- EF Core with PostgreSQL, migrations, transactions, and concurrency.
- Modular-monolith boundaries and architecture tests.
- SignalR notifications and a framework-free TypeScript client.
- Validation, Problem Details, authentication, authorization, caching, structured logging, tracing, metrics, health checks, and graceful shutdown.
- Kafka producers and consumers, at-least-once delivery, idempotency, outbox/inbox, retry, dead-letter topics, and observability.
- JSON integration events followed by Avro and Schema Registry evolution.
- Dapr pub/sub as an optional infrastructure adapter.
- Extraction of Barista and Kitchen workers with data ownership.
- Docker Compose development and Nomad deployment.

## 4. Curriculum Strategy

Use one linear learning branch with exactly 36 planned green lesson commits. Organize it into four independently useful phases. The implementation plan may split a lesson only when necessary to preserve a green, reviewable checkpoint, but the final curriculum remains within the approved 30–40 commit range.

### Phase 1: Rebuild Original Behavior

Build a working vertical slice early, then add depth:

1. .NET 10 solution, repository guidance, and verification baseline.
2. Minimal order endpoint and API contract.
3. Order domain model, pricing behavior, and focused unit tests.
4. EF Core/PostgreSQL persistence and migrations.
5. Querying fulfilled orders with the specification idea from the original source.
6. In-process request handling and validation.
7. Domain events within the process.
8. Barista preparation behavior.
9. Kitchen preparation and order completion behavior.
10. SignalR server and framework-free TypeScript client.
11. Configurable DataGen worker.
12. Docker Compose development environment and phase smoke test.

At the end of this phase, the observable order flow matches the original project while using supported .NET 10 patterns.

### Phase 2: Build a Real Modular Monolith

Improve boundaries and operational quality without changing the core business flow:

1. Split Counter, Barista, and Kitchen into module assemblies with a minimal shared kernel.
2. Add architecture tests that enforce dependency direction.
3. Introduce versioned API evolution and `201 Created` responses.
4. Standardize validation and failures with Problem Details.
5. Add JWT Bearer authentication backed by a containerized identity provider.
6. Add policy-based authorization with deterministic security tests.
7. Add Redis cache-aside, event-driven invalidation, and cache metrics for read models.
8. Add structured logging, health checks, and configuration validation.

### Phase 3: Reliable Event-Driven Integration

Introduce distributed messaging only after module boundaries are explicit:

1. Define integration-event contracts separate from domain events.
2. Publish and consume JSON events through Kafka.
3. Persist aggregate changes and outbox records in one transaction.
4. Publish outbox records reliably in a background service.
5. Add consumer inbox/idempotency.
6. Add bounded retry and dead-letter topics.
7. Propagate correlation and causation identifiers through HTTP, outbox, and Kafka headers.
8. Introduce Avro, Schema Registry, and compatible schema-evolution tests.
9. Add OpenTelemetry traces and metrics across process boundaries.
10. Add Dapr pub/sub as an optional adapter behind application-owned interfaces.

The design guarantees at-least-once delivery plus idempotent handling. It does not claim end-to-end exactly-once delivery.

### Phase 4: Capstone

1. Extract Barista into an independent worker with owned persistence and migrations.
2. Extract Kitchen into an independent worker with owned persistence and migrations.
3. Enforce contract-only communication and independent database ownership.
4. Add an end-to-end Docker Compose smoke test and hardened DataGen scenarios.
5. Add Nomad deployment material.
6. Complete CI, history auditing, and final architecture documentation.

## 5. Target Architecture

The initial reconstruction runs in one process. The improved modular monolith organizes each module around its own domain, application, infrastructure, and contracts. The host remains a composition root.

Dependency rules:

- Domain code depends on no web, persistence, broker, cache, or telemetry framework.
- Application code owns use cases and declares the ports it needs.
- Infrastructure implements persistence, messaging, identity, cache, and telemetry adapters.
- Integration contracts are stable and do not expose persistence entities.
- Modules cannot reference another module's infrastructure.
- Shared-kernel types require evidence of real shared semantics.

The capstone extracts Barista and Kitchen into workers while retaining the same application-owned contracts.

## 6. Data Flow

1. The API accepts a `PlaceOrder` request.
2. Validation rejects malformed or semantically invalid input.
3. The `Order` aggregate creates line items and enforces invariants.
4. One database transaction writes the aggregate and applicable outbox records.
5. The outbox publisher sends integration events to Kafka.
6. Barista and Kitchen consumers handle messages using at-least-once semantics.
7. Inbox records or idempotency keys prevent duplicate side effects.
8. Workers publish item-fulfilled events.
9. Counter applies concurrent item completion safely and updates the order status.
10. The query model/cache is refreshed, and SignalR sends a typed notification.

Inside one module/process, domain events use an in-process dispatcher. Across module/service boundaries, integration events use Kafka. Domain code never depends directly on MediatR, Confluent.Kafka, or Dapr.

## 7. Error Handling and Reliability

- HTTP failures use standardized Problem Details and appropriate status codes.
- Expected domain failures use explicit results or domain-specific errors instead of generic exceptions.
- Temporary dependency failures use bounded retries with backoff.
- Permanent message failures go to a dead-letter topic with actionable metadata.
- Kafka offsets are acknowledged only after business processing succeeds.
- Consumers stop accepting work and drain active work during graceful shutdown.
- Optimistic concurrency protects aggregate updates.
- Correlation and causation identifiers propagate across HTTP and messaging.
- Logs exclude secrets and sensitive payloads.
- Liveness and readiness represent different operational questions.

## 8. API Compatibility

Reconstruct `/v1` behavior before improving it. Any improvement that changes an observable contract must be versioned or accompanied by an explicit migration lesson. The improved create-order API returns `201 Created`, an order identifier, and a resource location without silently breaking the original client.

## 9. Persistence and Ownership

- Use PostgreSQL for production-like development and integration tests.
- Keep EF Core and Npgsql provider major versions compatible and verify the selected versions by restore, build, and tests.
- During the modular-monolith phase, modules may share a PostgreSQL server but own distinct schemas and migrations.
- During the capstone, extracted services own separate databases or logically independent database instances and never query another service's tables.
- Redis caches only query/read-model data. Command handling never treats cached aggregates as authoritative.

## 10. Security

- Do not implement a custom identity server.
- Use JWT Bearer authentication with a containerized standards-based identity provider for integration and demos.
- Use a test authentication handler for deterministic tests.
- Protect customer, fulfillment, and operational actions with policies rather than scattered role checks.
- Keep credentials out of Git; commit only samples and environment-variable names.

## 11. Observability

- Use Serilog for structured application logs.
- Use OpenTelemetry for distributed traces and metrics.
- Propagate trace context through Kafka headers.
- Record meaningful application and messaging measures such as order duration, outbox backlog, consumer failure count, dead-letter count, and Redis hit ratio.

## 12. Frontend and Data Generation

The frontend remains framework-free TypeScript. It teaches the SignalR connection lifecycle, typed messages, reconnect behavior, and rendering order status. Modernize the build tool in a dedicated lesson without turning the curriculum into a frontend-framework course.

DataGen preserves random order generation but adds finite-run mode, configurable rate, deterministic seed, and safe defaults. It supports demos and smoke tests without creating an uncontrolled infinite workload.

## 13. Deployment

- Docker Compose is the primary local environment.
- Add PostgreSQL, Kafka, Schema Registry, Redis, and the identity provider only when their lessons require them.
- Keep startup dependencies observable through health checks instead of relying only on startup order.
- Cover Nomad because it exists in the original source.
- Kubernetes is explicitly outside this curriculum.

## 14. Testing and Green-Commit Contract

Every lesson commit must build and pass all tests applicable at that point. No intentionally red commit is retained.

Testing layers:

- Domain unit tests for invariants and transitions.
- Application tests for use cases and ports.
- API functional tests with `WebApplicationFactory`.
- EF Core integration tests against PostgreSQL via containers.
- Architecture tests for module dependency rules.
- Kafka integration tests against a real broker container.
- Duplicate-delivery tests for inbox/idempotency.
- Failure-recovery tests for outbox publishing.
- Avro compatibility tests.
- A Docker Compose end-to-end smoke test.

The standard local verification sequence grows with the curriculum:

```text
dotnet restore
dotnet build --no-restore
dotnet test --no-build
npm ci
npm run build
```

CI runs build, unit tests, frontend build, and separate container-backed integration jobs. Before publishing the finished curriculum, an audit script checks out the lesson commits and runs the verification contract defined for each checkpoint.

## 15. Lesson and Commit Format

Use English for code identifiers and commit messages. Use Vietnamese for lesson documents.

Commit subject format:

```text
lesson(07): persist orders with EF Core and PostgreSQL
```

Each commit message body records purpose, verification, and the knowledge summary. Each commit adds or updates `docs/lessons/NN-topic.md` with:

- Mục tiêu bài học.
- Kiến thức cần có trước.
- Sơ đồ hoặc luồng được xây dựng.
- Các file quan trọng.
- Giải thích quyết định thiết kế.
- Lệnh build, run, và test.
- Kiến thức cần nhớ.
- Sai lầm thường gặp.
- Bài tập tự làm.
- Technical debt cố ý để lại.
- Liên kết bài trước và bài tiếp theo.

`README.md` is the curriculum index. Each phase receives a checkpoint tag. A commit contains only the lesson's coherent change; unrelated formatting and refactoring are excluded.

## 16. Branch and Publishing Strategy

- `main`: destination repository landing page and repository guidance.
- `original/dotnet7`: all 15 imported commits from the original source repository.
- `learning/dotnet10-rebuild`: orphan branch containing the linear green curriculum.
- `planning/dotnet10-curriculum`: design and implementation planning before execution.

Do not force-push the original/reference history. Publish the curriculum branch and lesson checkpoint tags only after the local history audit is green. Push to `https://github.com/haiducvu/coffeeshop-modular-fork.git` after verification.

## 17. Non-Goals

- A complete payment, inventory, loyalty, or identity product.
- A React, Vue, or other frontend-framework course.
- Kubernetes deployment.
- A custom identity provider.
- Using Kafka for every in-process interaction.
- Claiming end-to-end exactly-once behavior.
- Activating infrastructure solely because the original repository contains a dependency for it.

## 18. Success Criteria

The work is complete when:

- The original behavior is demonstrably reconstructed on .NET 10.
- The curriculum contains 36 planned coherent green lesson commits and remains within the approved 30–40 range if implementation constraints require a small adjustment.
- Every lesson has a Vietnamese document and an English commit message with purpose and verification.
- The final modular monolith enforces module boundaries.
- Kafka reliability lessons include outbox, inbox/idempotency, retry, dead-letter handling, Avro, and Schema Registry.
- The capstone runs Counter, Barista, and Kitchen with explicit data ownership.
- CI and local audits pass.
- Docker Compose provides a reproducible local environment.
- Nomad deployment material is documented.
- License, attribution, and the original reference are preserved.
- The verified branches and tags are pushed to the destination repository without rewriting the original/reference branch.
