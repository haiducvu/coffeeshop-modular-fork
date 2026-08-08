# CoffeeShop .NET 10 Curriculum Master Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and publish a 36-commit Vietnamese learning curriculum that reconstructs the original CoffeeShop behavior on .NET 10, hardens it as a modular monolith, and finishes with a reliable Kafka-based distributed capstone.

**Architecture:** Work on the orphan branch `learning/dotnet10-rebuild` while preserving the original 15 commits on `original/dotnet7`. Deliver four independently runnable phases: original behavior, modular-monolith hardening, reliable event integration, and service-extraction capstone. Every lesson is one coherent green commit with a Vietnamese lesson document and an English commit message.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core/Npgsql/PostgreSQL, xUnit, Testcontainers, MediatR, FluentValidation, SignalR, TypeScript/Vite, Redis, JWT Bearer/Keycloak, Serilog, OpenTelemetry, Confluent.Kafka, Avro/Schema Registry, Dapr, Docker Compose, and Nomad.

## Global Constraints

- Target `net10.0`; do not retain .NET 7 preview dependencies.
- Preserve original observable behavior before introducing versioned improvements.
- Keep domain and application code independent from ASP.NET Core, EF Core, Kafka, Dapr, Redis, and telemetry implementations.
- Use MediatR only for in-process request and domain-event dispatch; use Kafka for integration events.
- Guarantee at-least-once delivery plus idempotent handling; do not claim end-to-end exactly-once delivery.
- Use PostgreSQL-backed integration tests for persistence and broker-backed integration tests for Kafka.
- Keep each lesson commit buildable and all applicable tests green.
- Write lesson documents in Vietnamese; keep code identifiers and commit messages in English.
- Keep secrets out of Git and preserve original license and attribution.
- Push only after the complete history audit succeeds.

---

## Repository and Branch Setup

**Reference source:** `/Users/phuongtrinh/haivu/03_dontnet-2026/coffeeshop-modular`

**Working repository:** `/Users/phuongtrinh/haivu/dontnet-2026/coffeeshop-modular-fork`

**Branches:**

- `main`: destination landing page.
- `planning/dotnet10-curriculum`: approved spec and implementation plans.
- `original/dotnet7`: exact 15-commit original history.
- `learning/dotnet10-rebuild`: orphan curriculum history.

## Phase Plans

- Phase 1 detailed plan: `docs/superpowers/plans/2026-08-08-phase-1-original-behavior.md`
- Phase 2 detailed plan: create before Lesson 13 after the Phase 1 checkpoint passes.
- Phase 3 detailed plan: create before Lesson 21 after the Phase 2 checkpoint passes.
- Phase 4 detailed plan: create before Lesson 31 after the Phase 3 checkpoint passes.

Each child plan must repeat the global constraints, define exact files and interfaces, include red-green-refactor steps, and pass a placeholder/type-consistency review before execution.

## 36-Lesson Commit Map

### Phase 1 — Rebuild Original Behavior

#### Lesson 01: Bootstrap a verifiable .NET 10 solution

- **Commit:** `lesson(01): bootstrap the .NET 10 solution`
- **Deliverable:** Solution, API project, first test project, central build settings, license, attribution, curriculum index, and CI build/test workflow.
- **Proof:** `dotnet restore`, `dotnet build --no-restore`, and `dotnet test --no-build` pass.
- **Knowledge:** SDK selection, solution/project structure, nullable reference types, warnings-as-errors, deterministic builds, and green-commit discipline.

#### Lesson 02: Expose the original place-order HTTP contract

- **Commit:** `lesson(02): expose the place-order endpoint`
- **Deliverable:** `POST /v1/api/orders` accepts the original request shape through a thin vertical slice and returns the original success behavior.
- **Proof:** API functional tests cover success, JSON enum values, and malformed JSON.
- **Knowledge:** Minimal API route groups, transport contracts, endpoint tests, and separation between HTTP and business behavior.

#### Lesson 03: Model orders and menu pricing

- **Commit:** `lesson(03): model orders and menu pricing`
- **Deliverable:** Framework-free Order aggregate, LineItem entity, menu catalog, status transitions, and unit tests.
- **Proof:** Domain tests cover beverage/food classification, server-owned prices, line creation, and invalid empty orders.
- **Knowledge:** Aggregate roots, entities, invariants, factories, value semantics, and why clients cannot set prices.

#### Lesson 04: Persist orders with EF Core and PostgreSQL

- **Commit:** `lesson(04): persist orders with EF Core and PostgreSQL`
- **Deliverable:** DbContext, explicit mappings, repository port/adapter, migration, PostgreSQL Testcontainer fixture, and endpoint persistence.
- **Proof:** Integration tests save and reload an order with line items against PostgreSQL.
- **Knowledge:** DbContext lifetime, aggregate persistence, migrations, provider alignment, and integration-test isolation.

#### Lesson 05: Query fulfilled orders with specifications

- **Commit:** `lesson(05): query fulfilled orders with specifications`
- **Deliverable:** `GET /v1/api/fulfillment-orders`, a small query specification abstraction, and read-only EF query.
- **Proof:** Tests exclude in-progress orders, include line items, and avoid tracked query state.
- **Knowledge:** Query objects, specification trade-offs, eager loading, read models, and avoiding a generic repository dumping ground.

#### Lesson 06: Dispatch use cases and validate requests

- **Commit:** `lesson(06): dispatch use cases and validate requests`
- **Deliverable:** MediatR command/query handlers, current registration API, FluentValidation rules, and pipeline behavior.
- **Proof:** Handler and API tests cover valid requests and deterministic validation failures.
- **Knowledge:** CQRS-style dispatch, pipeline behaviors, validation boundaries, and third-party licensing/configuration awareness.

#### Lesson 07: Raise and dispatch domain events

- **Commit:** `lesson(07): dispatch in-process domain events`
- **Deliverable:** Domain-event collection on aggregates, application-owned dispatcher, post-save dispatch, and notification handlers.
- **Proof:** Unit tests verify event creation and application tests verify dispatch/clearing exactly once.
- **Knowledge:** Domain events versus integration events, event lifetime, transactional gaps, and why this implementation is intentionally still in-process.

#### Lesson 08: Simulate Barista preparation

- **Commit:** `lesson(08): process barista items asynchronously`
- **Deliverable:** Barista item model, order-in event handler, injectable delay/clock, preparation rules, and persistence.
- **Proof:** Tests use fake time/delay and do not wait real seconds.
- **Knowledge:** Async workflows, deterministic time, cancellation, and avoiding slow tests.

#### Lesson 09: Complete Kitchen items and orders

- **Commit:** `lesson(09): process kitchen items and fulfill orders`
- **Deliverable:** Kitchen preparation, item-fulfilled event handling, concurrent-safe order transition, and final order status.
- **Proof:** Tests cover partial completion, full completion, duplicate completion, and missing items.
- **Knowledge:** State machines, idempotent transitions, concurrency risks, and aggregate consistency.

#### Lesson 10: Stream typed updates with SignalR

- **Commit:** `lesson(10): stream order updates with SignalR`
- **Deliverable:** Typed hub contract, server broadcaster, vanilla TypeScript/Vite client, reconnect behavior, and frontend build.
- **Proof:** Server tests verify broadcasts and `npm run build` typechecks/bundles the client.
- **Knowledge:** Realtime boundaries, typed messages, connection lifecycle, CORS, and frontend/backend contract alignment.

#### Lesson 11: Generate deterministic demo orders

- **Commit:** `lesson(11): add a configurable order generator`
- **Deliverable:** Worker/CLI with finite count, configurable rate, deterministic seed, cancellation, and safe defaults.
- **Proof:** Tests use a fake HTTP handler and fixed seed; no infinite workload runs in tests.
- **Knowledge:** Hosted services, `HttpClientFactory`, options validation, deterministic randomness, and graceful cancellation.

#### Lesson 12: Run the original behavior with Docker Compose

- **Commit:** `lesson(12): compose the original coffee shop flow`
- **Deliverable:** Multi-stage images, PostgreSQL/API/client/DataGen Compose services, health checks, migrations, and Phase 1 smoke script.
- **Proof:** Solution tests and frontend build pass; when Docker is available, smoke test places and fulfills an order.
- **Knowledge:** Container networking, configuration via environment, readiness, reproducible local environments, and phase checkpoints.

### Phase 2 — Build a Real Modular Monolith

#### Lesson 13: Separate modules into assemblies

- **Commit:** `lesson(13): separate the coffee shop modules`
- **Deliverable:** Counter, Barista, Kitchen, Contracts, and minimal SharedKernel projects with explicit composition.
- **Proof:** Existing behavior tests remain green after moves.
- **Knowledge:** Module ownership, public surfaces, dependency direction, and composition roots.

#### Lesson 14: Enforce architecture boundaries

- **Commit:** `lesson(14): enforce module dependency rules`
- **Deliverable:** Architecture-test project and rules preventing cross-module infrastructure/domain references.
- **Proof:** Positive rules pass and a documented mutation demonstrates the failure message.
- **Knowledge:** Fitness functions and preventing architecture erosion.

#### Lesson 15: Evolve the create-order API

- **Commit:** `lesson(15): add a resource-oriented order API`
- **Deliverable:** Versioned improved endpoint returning `201 Created`, order ID, and resource URL while preserving `/v1` behavior.
- **Proof:** Contract tests cover both versions.
- **Knowledge:** HTTP semantics, compatibility, contract versioning, and migration notes.

#### Lesson 16: Standardize validation and failures

- **Commit:** `lesson(16): standardize API failures with Problem Details`
- **Deliverable:** Problem Details mapping for validation, not-found, conflict, and unexpected failures.
- **Proof:** Functional tests assert status, type, title, trace ID, and safe details.
- **Knowledge:** Error taxonomies and observable API contracts.

#### Lesson 17: Authenticate with an external identity provider

- **Commit:** `lesson(17): authenticate API clients with JWT bearer`
- **Deliverable:** JWT Bearer integration, containerized identity provider configuration, and deterministic test authentication.
- **Proof:** Anonymous/authenticated functional tests pass without a live identity provider; Compose demo validates real tokens.
- **Knowledge:** Authentication boundary, token validation, issuer/audience, and why the app does not issue identities.

#### Lesson 18: Authorize coffee shop operations

- **Commit:** `lesson(18): authorize operations with policies`
- **Deliverable:** Policies for customer, fulfillment reader, and operator actions.
- **Proof:** Matrix tests cover allowed and forbidden identities.
- **Knowledge:** Policy-based authorization and least privilege.

#### Lesson 19: Cache fulfillment read models

- **Commit:** `lesson(19): cache fulfillment read models with Redis`
- **Deliverable:** Cache-aside query adapter, TTL, event-driven invalidation, and hit/miss metrics.
- **Proof:** Unit tests cover hit/miss/invalidation; container test uses Redis.
- **Knowledge:** Cache consistency, read-model caching, stampede/staleness risks, and why commands bypass cache.

#### Lesson 20: Add operational foundations

- **Commit:** `lesson(20): add structured logs and health checks`
- **Deliverable:** Serilog, validated options, separate liveness/readiness, redaction, and Phase 2 checkpoint docs.
- **Proof:** Tests cover missing configuration and health-state semantics.
- **Knowledge:** Structured logging, configuration failure, health probes, and operational contracts.

### Phase 3 — Reliable Event-Driven Integration

#### Lesson 21: Separate integration contracts

- **Commit:** `lesson(21): define versioned integration events`
- **Deliverable:** Broker-neutral envelopes and versioned events distinct from domain events.
- **Proof:** Serialization/contract tests protect names and required fields.
- **Knowledge:** Event ownership, public contracts, versioning, correlation, and causation.

#### Lesson 22: Publish and consume Kafka JSON events

- **Commit:** `lesson(22): exchange integration events through Kafka`
- **Deliverable:** Application ports, Confluent producer/hosted consumer adapters, topic configuration, and JSON serialization.
- **Proof:** Broker container test round-trips an event and shuts down cleanly.
- **Knowledge:** Topics, keys, partitions, consumer groups, idempotent producer settings, offsets, and hosted poll loops.

#### Lesson 23: Save orders and outbox records atomically

- **Commit:** `lesson(23): persist integration events in an outbox`
- **Deliverable:** Outbox entity/table, mapper from domain outcome to integration event, and same-transaction persistence.
- **Proof:** PostgreSQL tests prove both order/outbox commit or neither commits.
- **Knowledge:** Dual-write failure and transactional outbox.

#### Lesson 24: Publish the outbox reliably

- **Commit:** `lesson(24): publish pending outbox messages`
- **Deliverable:** Background publisher, leasing/batching, retry metadata, and sent marking.
- **Proof:** Tests cover crash before publish, publish failure, restart, and eventual sent state.
- **Knowledge:** Polling publishers, duplicate windows, leases, and observability.

#### Lesson 25: Make consumers idempotent with an inbox

- **Commit:** `lesson(25): deduplicate consumed messages with an inbox`
- **Deliverable:** Inbox table and transaction coupling between message record and business side effects.
- **Proof:** Duplicate delivery produces one side effect.
- **Knowledge:** At-least-once consequences and idempotent consumers.

#### Lesson 26: Retry and dead-letter failed messages

- **Commit:** `lesson(26): handle poison messages with retry and dead letters`
- **Deliverable:** Error classification, bounded retry topics/backoff, dead-letter envelope, and replay guidance.
- **Proof:** Tests distinguish transient recovery from permanent dead-letter routing.
- **Knowledge:** Poison messages, retry budgets, DLT operations, and offset discipline.

#### Lesson 27: Propagate correlation and causation

- **Commit:** `lesson(27): correlate HTTP and Kafka workflows`
- **Deliverable:** Correlation/causation IDs across requests, outbox rows, headers, logs, and notifications.
- **Proof:** End-to-end integration test asserts identifier continuity.
- **Knowledge:** Traceability versus business identity.

#### Lesson 28: Evolve events with Avro schemas

- **Commit:** `lesson(28): govern event schemas with Avro`
- **Deliverable:** Avro contracts, Schema Registry serializers, compatibility policy, and evolution tests.
- **Proof:** Compatible change passes; breaking fixture fails with an explanatory assertion.
- **Knowledge:** Schema-first contracts, compatibility modes, defaults, and rollout ordering.

#### Lesson 29: Trace and measure distributed work

- **Commit:** `lesson(29): observe distributed order processing`
- **Deliverable:** OpenTelemetry traces/metrics for HTTP, EF, Kafka, outbox, inbox, cache, and business duration.
- **Proof:** Tests/listener assertions verify span relationships and key metrics.
- **Knowledge:** Trace context, spans, metrics cardinality, and actionable telemetry.

#### Lesson 30: Add Dapr as an optional adapter

- **Commit:** `lesson(30): provide a Dapr pubsub adapter`
- **Deliverable:** Dapr implementation behind the existing integration-event ports and configuration switch.
- **Proof:** Contract tests run against both Kafka and Dapr adapter surfaces; Kafka remains default.
- **Knowledge:** Sidecar trade-offs, infrastructure substitution, and preventing framework leakage.

### Phase 4 — Distributed Capstone

#### Lesson 31: Extract the Barista worker

- **Commit:** `lesson(31): extract barista into a worker service`
- **Deliverable:** Independent host, consumer, persistence, migrations, image, and owned database.
- **Proof:** Contract/integration tests prove order-in to item-fulfilled flow.
- **Knowledge:** Service extraction using existing seams and deployment independence.

#### Lesson 32: Extract the Kitchen worker

- **Commit:** `lesson(32): extract kitchen into a worker service`
- **Deliverable:** Independent Kitchen host and owned persistence matching Barista reliability patterns.
- **Proof:** Contract/integration tests cover food preparation and duplicate delivery.
- **Knowledge:** Reusing patterns without sharing internal implementations.

#### Lesson 33: Enforce service data ownership

- **Commit:** `lesson(33): enforce independent service data ownership`
- **Deliverable:** Separate connection settings/migrations, removed shared-table access, and ownership tests/documentation.
- **Proof:** Architecture and integration tests fail if a service reaches another service's database objects.
- **Knowledge:** Database-per-service, eventual consistency, and ownership boundaries.

#### Lesson 34: Exercise the distributed flow end to end

- **Commit:** `lesson(34): exercise the distributed coffee shop flow`
- **Deliverable:** Complete Compose topology, hardened DataGen scenarios, deterministic smoke test, and failure-demo scenarios.
- **Proof:** Finite order batch completes with no lost orders and expected duplicate protection.
- **Knowledge:** System testing, fault injection, eventual assertions, and demo ergonomics.

#### Lesson 35: Deploy the capstone with Nomad

- **Commit:** `lesson(35): deploy the coffee shop with Nomad`
- **Deliverable:** Parameterized Nomad jobs, health checks, secrets/config documentation, and rollout/rollback guide.
- **Proof:** Static validation succeeds where Nomad CLI is available; rendered config is documented otherwise.
- **Knowledge:** Scheduling, service health, configuration, rolling updates, and rollback.

#### Lesson 36: Audit and publish the curriculum

- **Commit:** `lesson(36): complete the curriculum and history audit`
- **Deliverable:** Full CI, lesson index, C4 diagrams, architecture decision summary, history-audit script, tags, and contributor guidance.
- **Proof:** Clean clone verification passes; history audit checks every lesson contract; destination branches/tags are ready to push.
- **Knowledge:** Maintaining educational repositories, reproducibility, architecture communication, and release discipline.

## Master Verification

- [ ] Verify the planning branch is clean and contains approved spec plus plans.
- [ ] Preserve original history as `original/dotnet7` and verify it has 15 commits.
- [ ] Create `learning/dotnet10-rebuild` as an orphan branch.
- [ ] Execute Phase 1 plan and tag `phase-1-original-behavior`.
- [ ] Write/review/execute Phase 2 plan and tag `phase-2-modular-monolith`.
- [ ] Write/review/execute Phase 3 plan and tag `phase-3-reliable-events`.
- [ ] Write/review/execute Phase 4 plan and tag `phase-4-capstone`.
- [ ] Run clean-clone and lesson-history audits.
- [ ] Push `planning/dotnet10-curriculum`, `original/dotnet7`, `learning/dotnet10-rebuild`, and checkpoint tags without force.
