# Phase 3 Reliable Event-Driven Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Evolve the .NET 10 CoffeeShop modular monolith from in-process cross-module fulfillment to a reliable Kafka workflow with versioned contracts, Transactional Outbox, idempotent Inbox, bounded retry/DLT, Avro governance, OpenTelemetry, and an optional Dapr adapter.

**Architecture:** The API remains one deployable composition root through Lesson 30. Modules own business state plus their PostgreSQL Outbox/Inbox tables; broker-neutral contracts and ports isolate them from Confluent Kafka, Schema Registry, OpenTelemetry exporters, and Dapr. Kafka becomes the real workflow only in Lesson 25 and guarantees at-least-once delivery with idempotent handling, never end-to-end exactly-once.

**Tech Stack:** .NET SDK 10, C# 14, ASP.NET Core 10, EF Core 10/Npgsql, PostgreSQL 17, Confluent.Kafka 2.15.0, Confluent Schema Registry/Avro 2.15.0, Testcontainers.Kafka 4.14.0, xUnit 2.9.3, OpenTelemetry .NET 1.18.0, Dapr .NET SDK 1.18.5, Docker Compose, Kafka KRaft.

**Spec:** `docs/superpowers/specs/2026-08-25-phase-3-reliable-events-design.md`

## Global Constraints

- Target `net10.0`; keep nullable analysis, implicit usings, deterministic builds, and warnings-as-errors enabled by the existing repository settings.
- Pin every NuGet dependency centrally in `Directory.Packages.props`; run strict restore and do not suppress audit warnings.
- Preserve `/v1`, `/v2`, JWT authorization, SignalR, Redis caching, health, and structured-log behavior from Phases 1 and 2.
- Keep IntegrationContracts framework/broker-free and keep modules free of Kafka, Schema Registry, Dapr, and exporter packages.
- Keep customer identity and secrets out of integration events, logs, metric labels, and failure metadata.
- Use broker/database-backed tests for reliability claims; use fake time for retry tests and bounded polling for smoke tests.
- Each numbered task below is exactly one lesson commit with the exact subject shown. Red TDD states are never committed.
- Write `docs/lessons/NN-*.md` in Vietnamese with purpose, behavior, implementation narrative, verification evidence, failure scenarios, and a knowledge summary.
- Push each green lesson immediately to `origin/learning/dotnet10-rebuild`, then prove local `HEAD` equals the remote branch hash.

## Shared Green Gate

Run this gate from a clean lesson worktree before every lesson commit:

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx -c Release --no-restore
dotnet test CoffeeShop.slnx -c Release --no-build
npm ci --prefix src/CoffeeShop.SignalRClient
npm run build --prefix src/CoffeeShop.SignalRClient
docker compose config --quiet
docker compose --profile demo --profile identity --profile messaging build
bash -n scripts/*.sh tests/scripts/*.sh
jq empty deploy/keycloak/coffeeshop-realm.json
git diff --check
```

From Lesson 25 onward also run the Phase 3 smoke script from fresh volumes. Add `schema`, `observability`, and `dapr` profiles to the build/smoke gate beginning in their respective lessons. Always tear down with:

```bash
docker compose down --volumes --remove-orphans
```

---

### Task 21: Define versioned integration contracts

**Files:**

- Create: `src/CoffeeShop.IntegrationContracts/CoffeeShop.IntegrationContracts.csproj`
- Create: `src/CoffeeShop.IntegrationContracts/IntegrationEventEnvelope.cs`
- Create: `src/CoffeeShop.IntegrationContracts/IIntegrationEvent.cs`
- Create: `src/CoffeeShop.IntegrationContracts/Orders/OrderPlacedV1.cs`
- Create: `src/CoffeeShop.IntegrationContracts/Orders/OrderItemPreparedV1.cs`
- Create: `tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj`
- Create: `tests/CoffeeShop.MessagingTests/Contracts/IntegrationContractTests.cs`
- Create: `tests/CoffeeShop.MessagingTests/Fixtures/order-placed-v1.json`
- Create: `tests/CoffeeShop.MessagingTests/Fixtures/order-item-prepared-v1.json`
- Modify: `CoffeeShop.slnx`
- Modify: `tests/CoffeeShop.ArchitectureTests/ModuleDependencyTests.cs`
- Create: `docs/lessons/21-versioned-integration-events.md`

**Interfaces:**

- Produces `IIntegrationEvent` with static `EventType` and `EventVersion` metadata.
- Produces `IntegrationEventEnvelope<TPayload>` constrained to `IIntegrationEvent`.
- Produces immutable `OrderPlacedV1`, `OrderLineItemV1`, and `OrderItemPreparedV1` payloads.
- Wire names are `coffeeshop.order-placed` and `coffeeshop.order-item-prepared`, both Version 1.

- [ ] **Step 1: Write failing contract and architecture tests**

Add tests that construct the exact envelope below, serialize with `JsonSerializerDefaults.Web`, compare it semantically with the checked-in fixture, deserialize it, and assert the stable event name/version. Add an architecture assertion that IntegrationContracts references no repository project and no non-BCL package.

```csharp
public sealed record IntegrationEventEnvelope<TPayload>(
    Guid MessageId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    TPayload Payload) where TPayload : IIntegrationEvent;

public interface IIntegrationEvent
{
    static abstract string EventType { get; }
    static abstract int EventVersion { get; }
}
```

Expected payload signatures:

```csharp
public sealed record OrderPlacedV1(
    Guid OrderId,
    IReadOnlyList<OrderLineItemV1> Items) : IIntegrationEvent;

public sealed record OrderLineItemV1(
    Guid LineItemId,
    string ItemType,
    string Station);

public sealed record OrderItemPreparedV1(
    Guid OrderId,
    Guid LineItemId,
    string ItemType,
    string Station,
    string MadeBy,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
```

- [ ] **Step 2: Prove the tests fail for missing projects/types**

Run:

```bash
dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj
```

Expected: build/test failure because the contract project and types are not yet implemented.

- [ ] **Step 3: Implement the minimal broker-neutral contract assembly**

Create a plain `Microsoft.NET.Sdk` project targeting `net10.0`. Add the records exactly as above; use string item/station values to avoid publishing internal enum ordinals. Do not reference `CoffeeShop.Contracts`, Kafka, ASP.NET Core, EF Core, or serialization packages.

- [ ] **Step 4: Make contract tests green and validate public shape**

Run:

```bash
dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj
dotnet test tests/CoffeeShop.ArchitectureTests/CoffeeShop.ArchitectureTests.csproj
```

Expected: fixtures round-trip, required properties/names remain stable, architecture tests pass.

- [ ] **Step 5: Write the Vietnamese lesson and run the Shared Green Gate**

Explain domain events versus integration events, semantic versioning, minimal payloads, why loyalty/location/source are excluded, and how golden fixtures detect accidental breaking changes.

- [ ] **Step 6: Commit and push Lesson 21**

```bash
git add CoffeeShop.slnx src/CoffeeShop.IntegrationContracts tests/CoffeeShop.MessagingTests tests/CoffeeShop.ArchitectureTests/ModuleDependencyTests.cs docs/lessons/21-versioned-integration-events.md
git commit -m "lesson(21): define versioned integration events" \
  -m "Purpose: Separate public integration contracts from in-process domain events with stable semantic names and Version 1 fixtures." \
  -m "Verification: Strict restore, Release build, full tests, frontend build, Compose validation/image build, architecture checks, and diff checks pass." \
  -m "Knowledge: Covers event ownership, contract minimization, envelopes, versioning, correlation, causation, and compatibility fixtures."
git push origin learning/dotnet10-rebuild
git rev-parse HEAD
git rev-parse origin/learning/dotnet10-rebuild
```

Expected: both hashes are identical.

---

### Task 22: Exchange JSON integration events through Kafka

**Files:**

- Create: `src/CoffeeShop.Messaging.Abstractions/CoffeeShop.Messaging.Abstractions.csproj`
- Create: `src/CoffeeShop.Messaging.Abstractions/IIntegrationEventPublisher.cs`
- Create: `src/CoffeeShop.Messaging.Abstractions/IIntegrationEventHandler.cs`
- Create: `src/CoffeeShop.Messaging.Abstractions/IntegrationMessageContext.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/CoffeeShop.Messaging.Kafka.csproj`
- Create: `src/CoffeeShop.Messaging.Kafka/KafkaMessagingOptions.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/KafkaTopicResolver.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/KafkaHeaderNames.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/JsonIntegrationEventCodec.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/KafkaIntegrationEventPublisher.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/KafkaConsumerWorker.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/KafkaServiceCollectionExtensions.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/KafkaCollection.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/KafkaFixture.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/KafkaJsonRoundTripTests.cs`
- Modify: `Directory.Packages.props`, `CoffeeShop.slnx`, `compose.yaml`
- Modify: `src/CoffeeShop.Api/CoffeeShop.Api.csproj`, `src/CoffeeShop.Api/Program.cs`, `src/CoffeeShop.Api/appsettings.json`
- Create: `src/CoffeeShop.Api/Health/KafkaReadinessHealthCheck.cs`
- Create: `docs/lessons/22-kafka-json-transport.md`

**Interfaces:**

```csharp
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TPayload>(
        string key,
        IntegrationEventEnvelope<TPayload> message,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent;
}

public interface IIntegrationEventHandler<in TPayload>
    where TPayload : IIntegrationEvent
{
    Task HandleAsync(
        IntegrationEventEnvelope<TPayload> message,
        IntegrationMessageContext context,
        CancellationToken cancellationToken);
}

public sealed record IntegrationMessageContext(
    string ConsumerRole,
    string Source,
    int DeliveryAttempt);
```

- [ ] **Step 1: Add failing codec, topic, header, lifecycle, and real-broker tests**

Assert JSON uses camel case, canonical UUIDs, ISO-8601 UTC, required envelope/header identity, and tolerates additive fields. With `KafkaBuilder().Build()`, start a random-port broker in an xUnit collection fixture, publish `OrderPlacedV1` keyed by `OrderId`, consume from a run-specific group, manually commit, cancel the worker, and assert clean disposal.

- [ ] **Step 2: Run focused tests and confirm the intended failure**

```bash
dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj --filter FullyQualifiedName~Kafka
dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj
```

Expected: failure because ports, codec, producer, consumer, and fixture do not exist.

- [ ] **Step 3: Add centrally pinned dependencies and minimal adapters**

Pin `Confluent.Kafka` `2.15.0` and `Testcontainers.Kafka` `4.14.0`. Configure producer `EnableIdempotence=true`, `Acks=All`; configure consumer `EnableAutoCommit=false`, `AutoOffsetReset=Earliest`. Implement topic mapping:

```text
coffeeshop.order-placed:1         -> coffeeshop.orders.v1
coffeeshop.order-item-prepared:1 -> coffeeshop.preparation.v1
```

The worker must call `Close()` during orderly cancellation. Kafka types stay inside the adapter and integration-test project.

- [ ] **Step 4: Add optional runtime configuration and Compose messaging profile**

Add validated options for bootstrap servers and group prefixes. Add an official `apache/kafka:4.1.1` single-node KRaft service under profile `messaging`, with internal `kafka:19092` and random/configurable loopback host port. Kafka readiness is registered only when messaging is enabled; liveness remains process-only.

- [ ] **Step 5: Run focused and full gates**

Run the two focused test projects, then the Shared Green Gate plus:

```bash
docker compose --profile messaging up -d kafka
docker compose --profile messaging ps
docker compose down --volumes --remove-orphans
```

Expected: one JSON event round-trips through a real broker, offset is committed manually, cancellation completes, Compose broker becomes ready.

- [ ] **Step 6: Document, commit, and push Lesson 22**

```bash
git add .
git commit -m "lesson(22): exchange integration events through Kafka" \
  -m "Purpose: Introduce broker-neutral messaging ports and a Confluent Kafka JSON adapter without changing the order workflow." \
  -m "Verification: Real Kafka round-trip, manual-offset lifecycle tests, strict solution gates, frontend build, and messaging Compose checks pass." \
  -m "Knowledge: Covers topics, keys, partitions, consumer groups, producer idempotence, manual offsets, codecs, and hosted consumer shutdown."
git push origin learning/dotnet10-rebuild
```

Verify local/remote hashes match.

---

### Task 23: Persist Counter integration events atomically

**Files:**

- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Outbox/CounterOutboxMessage.cs`
- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Outbox/CounterOutboxMessageConfiguration.cs`
- Create: `src/CoffeeShop.Modules.Counter/Application/Outbox/ICounterOutboxWriter.cs`
- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Outbox/CounterOutboxWriter.cs`
- Create: `src/CoffeeShop.Modules.Counter/Application/Orders/PlaceOrder/OrderPlacedIntegrationEventMapper.cs`
- Modify: `src/CoffeeShop.Modules.Counter/Application/Orders/PlaceOrder/PlaceOrderHandler.cs`
- Modify: `src/CoffeeShop.Modules.Counter/Infrastructure/Persistence/CounterDbContext.cs`
- Modify: `src/CoffeeShop.Modules.Counter/CounterModuleServiceCollectionExtensions.cs`
- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Persistence/Migrations/*_AddCounterOutbox.cs`
- Create: `tests/CoffeeShop.IntegrationTests/CounterOutboxAtomicityTests.cs`
- Modify: application tests/fakes that construct `PlaceOrderHandler`
- Create: `docs/lessons/23-transactional-outbox.md`

**Interfaces:**

```csharp
internal interface ICounterOutboxWriter
{
    void Enqueue(OrderPlacedV1 payload, DateTimeOffset occurredAtUtc);
}
```

`CounterOutboxMessage` stores `MessageId`, `EventType`, `EventVersion`, canonical `EnvelopeJson`, `OccurredAtUtc`, correlation/causation, trace parent/state, attempts, scheduling, lease, published time, and bounded error code.

- [ ] **Step 1: Write failing PostgreSQL atomicity tests**

Test that successful placement persists exactly one order and one Outbox row containing all line-item IDs but no loyalty ID. Force `SaveChanges` failure with an invalid Outbox value and assert neither order nor Outbox remains. Assert the in-process fulfillment behavior still runs in this lesson.

- [ ] **Step 2: Verify red state**

```bash
dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj --filter FullyQualifiedName~CounterOutboxAtomicityTests
```

Expected: missing Outbox model/writer/migration.

- [ ] **Step 3: Map the accepted aggregate to one minimal event**

After `Order.Place`, create `OrderPlacedV1` from `order.Id` and every domain line item using its stable GUID and symbolic item/station name. Generate one envelope identity; use it as the initial correlation ID until the explicit HTTP correlation policy in Lesson 27. Track order and Outbox through the same `CounterDbContext`, then call one `SaveChangesAsync`.

- [ ] **Step 4: Add EF configuration and migration**

Map to `counter.outbox_messages`; use bounded varchar columns for event/error identifiers, `jsonb` for canonical envelope, indexes on `(published_at_utc, next_attempt_at_utc)` and lease expiry, and no Kafka-specific columns. Ensure the migration starts from a fresh PostgreSQL volume.

- [ ] **Step 5: Make atomicity tests and full gates green**

Run focused PostgreSQL tests, `ModuleSchemaTests`, then the Shared Green Gate. Inspect the migration SQL to confirm both business and Outbox data live in schema `counter`.

- [ ] **Step 6: Document, commit, and push Lesson 23**

```bash
git add .
git commit -m "lesson(23): persist integration events in an outbox" \
  -m "Purpose: Remove the order/Kafka dual-write risk by storing OrderPlacedV1 beside the order in one PostgreSQL transaction." \
  -m "Verification: PostgreSQL commit/rollback tests, schema migration tests, existing behavior tests, and all repository gates pass." \
  -m "Knowledge: Covers dual-write failure, canonical Outbox storage, local atomicity, data minimization, and migration design."
git push origin learning/dotnet10-rebuild
```

Verify local/remote hashes match.

---

### Task 24: Publish leased Outbox batches

**Files:**

- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Outbox/CounterOutboxOptions.cs`
- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Outbox/CounterOutboxStore.cs`
- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Outbox/CounterOutboxPublisher.cs`
- Create: `src/CoffeeShop.Modules.Counter/Infrastructure/Outbox/CounterOutboxWorker.cs`
- Modify: `src/CoffeeShop.Modules.Counter/CounterModuleServiceCollectionExtensions.cs`
- Modify: `src/CoffeeShop.Api/appsettings.json`, `compose.yaml`
- Create: `tests/CoffeeShop.ApplicationTests/OutboxPublisherTests.cs`
- Create: `tests/CoffeeShop.IntegrationTests/CounterOutboxLeaseTests.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/CounterOutboxKafkaTests.cs`
- Create: `docs/lessons/24-outbox-publisher.md`

**Interfaces:**

```csharp
internal interface ICounterOutboxStore
{
    Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimBatchAsync(
        Guid leaseId, int batchSize, DateTimeOffset now, DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);
    Task MarkPublishedAsync(Guid messageId, Guid leaseId, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid messageId, Guid leaseId, string safeErrorCode,
        DateTimeOffset nextAttemptAt, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing lease/publisher tests**

Cover `FOR UPDATE SKIP LOCKED` competition, bounded batch size, unexpired lease exclusion, expired lease reclaim, publish failure scheduling, conditional sent marking, and the crash window where a broker publish succeeds but `PublishedAtUtc` is not saved and the message is later published again.

- [ ] **Step 2: Verify focused red state**

```bash
dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj --filter FullyQualifiedName~CounterOutboxLeaseTests
dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj --filter FullyQualifiedName~CounterOutboxKafkaTests
```

- [ ] **Step 3: Implement short claims and publish outside DB transactions**

Claim rows and assign lease in one short transaction; commit before calling `IIntegrationEventPublisher`. Mark success only when the same lease is still owned. On failure increment attempts, store an allow-listed error code, release the lease, and schedule bounded retry. Never hold an EF transaction during Kafka I/O.

- [ ] **Step 4: Register the background worker as shadow publication**

Enable it only when messaging is configured. Keep the Phase 2 in-process Barista/Kitchen/Counter handlers active. Configure batch size, poll interval, lease duration, and transport retry delay through validated options and `TimeProvider`.

- [ ] **Step 5: Prove restart/duplicate behavior and run full gates**

The real-broker test must show a pending row becomes published, while the simulated post-publish crash can emit a duplicate without losing the row. Run the Shared Green Gate and a fresh-volume messaging Compose smoke that places an order and observes the shadow Kafka record while HTTP fulfillment remains unchanged.

- [ ] **Step 6: Document, commit, and push Lesson 24**

```bash
git add .
git commit -m "lesson(24): publish pending outbox messages" \
  -m "Purpose: Reliably drain Counter Outbox rows with bounded leasing and publish them to Kafka outside database transactions." \
  -m "Verification: Lease competition, failure/restart, duplicate-window, real-broker, solution, frontend, and Compose gates pass." \
  -m "Knowledge: Covers polling publishers, SKIP LOCKED, leases, crash windows, transport retry, and at-least-once publication."
git push origin learning/dotnet10-rebuild
```

Verify local/remote hashes match.

---

### Task 25: Cut over to Inbox-protected Kafka fulfillment

**Files:**

- Create matching Inbox entity/configuration/store files under each module's `Infrastructure/Inbox/`
- Create Barista/Kitchen Outbox entity/configuration/writer/publisher files under each module's `Infrastructure/Outbox/`
- Create: `src/CoffeeShop.Modules.Barista/Application/HandleOrderPlacedIntegrationEvent.cs`
- Create: `src/CoffeeShop.Modules.Kitchen/Application/HandleOrderPlacedIntegrationEvent.cs`
- Create: `src/CoffeeShop.Modules.Counter/Application/Orders/HandleOrderItemPreparedIntegrationEvent.cs`
- Modify Barista/Kitchen repositories so preparation state and outgoing Outbox rows share one `SaveChangesAsync`
- Modify all three module `DbContext` and service-registration files
- Create migrations `*_AddMessagingInboxAndOutbox.cs` in Barista/Kitchen and `*_AddCounterInbox.cs` in Counter
- Modify: `src/CoffeeShop.Api/Program.cs`, `compose.yaml`
- Create: `tests/CoffeeShop.IntegrationTests/InboxIdempotencyTests.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/KafkaFulfillmentWorkflowTests.cs`
- Create: `scripts/phase-3-smoke.sh`
- Create: `tests/scripts/phase-3-smoke-tests.sh`
- Create: `docs/lessons/25-idempotent-inbox.md`

**Interfaces:**

```csharp
internal interface IModuleInbox
{
    Task<InboxDecision> BeginAsync(
        string handlerName, Guid messageId, string eventType, int eventVersion,
        DateTimeOffset receivedAtUtc, CancellationToken cancellationToken);
    Task CompleteAsync(string handlerName, Guid messageId,
        DateTimeOffset processedAtUtc, CancellationToken cancellationToken);
}

internal enum InboxDecision { New, Duplicate }
```

Consumer roles/groups are `barista`, `kitchen`, and `counter`. Barista/Kitchen consume `OrderPlacedV1`; Counter consumes `OrderItemPreparedV1`.

- [ ] **Step 1: Write failing duplicate and end-to-end workflow tests**

Deliver the same envelope twice and assert one persisted preparation item, one prepared Outbox event, and one Counter line-item completion. Add a real Kafka test that places an order, waits boundedly for both station consumers, waits for Counter completion, and confirms the HTTP order contract, SignalR update, and Redis invalidation behavior remain intact.

- [ ] **Step 2: Verify the focused tests fail before cutover**

```bash
dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj --filter FullyQualifiedName~InboxIdempotencyTests
dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj --filter FullyQualifiedName~KafkaFulfillmentWorkflowTests
```

- [ ] **Step 3: Implement module-local Inbox transactions**

Use unique key `(handler_name, message_id)`. For a new delivery, insert Inbox, mutate only the owning module's state, enqueue resulting Outbox messages, mark Inbox complete, and commit one local transaction. A duplicate is a successful no-op. A failed business transaction rolls back the Inbox insert and all effects.

- [ ] **Step 4: Map station results without cross-module calls**

Barista/Kitchen filter their items by symbolic station, perform existing preparation delay/policy, persist items, and enqueue `OrderItemPreparedV1` preserving `LineItemId`, `MadeBy`, and completion time. Counter idempotently calls existing order completion logic, which still emits local `OrderUpdated` for SignalR/cache effects.

- [ ] **Step 5: Perform the single atomic composition cutover**

Remove only Barista/Kitchen `IDomainEventHandler<OrderItemAccepted>` and Counter `IDomainEventHandler<OrderItemPrepared>` cross-module registrations. Retain local SignalR/cache handlers. Register Kafka business consumers and all module Outbox workers. Make Kafka part of default Compose startup; do not leave a configuration that executes both preparation paths.

- [ ] **Step 6: Prove duplicate safety, workflow behavior, and fresh-state migrations**

Run focused tests, all solution tests, then:

```bash
tests/scripts/phase-3-smoke-tests.sh
docker compose down --volumes --remove-orphans
./scripts/phase-3-smoke.sh
```

The smoke must use bounded polling, authenticate where configured, place a mixed order, observe eventual fulfillment, and fail clearly on timeout. Then run the Shared Green Gate.

- [ ] **Step 7: Document, commit, and push Lesson 25**

```bash
git add .
git commit -m "lesson(25): deduplicate consumed messages with an inbox" \
  -m "Purpose: Make Kafka the real fulfillment path while coupling each module's Inbox record, business effect, and outgoing Outbox atomically." \
  -m "Verification: Duplicate-delivery, PostgreSQL atomicity, real Kafka workflow, API/SignalR/cache regression, fresh Compose smoke, and full gates pass." \
  -m "Knowledge: Covers at-least-once consequences, consumer groups, idempotent consumers, local transactions, manual offsets, and safe cutover."
git push origin learning/dotnet10-rebuild
```

Verify local/remote hashes match.

---

### Task 26: Route transient failures through retry topics and poison messages to DLT

**Files:**

- Create: `src/CoffeeShop.Messaging.Abstractions/IntegrationFailure.cs`
- Create: `src/CoffeeShop.Messaging.Abstractions/IIntegrationFailureClassifier.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/Retry/KafkaRetryOptions.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/Retry/KafkaRetryRouter.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/Retry/RetryTopicResolver.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/Retry/DeadLetterMetadata.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/Retry/IRetryDelay.cs`
- Modify: `src/CoffeeShop.Messaging.Kafka/KafkaConsumerWorker.cs`
- Modify: `src/CoffeeShop.Messaging.Kafka/KafkaHeaderNames.cs`
- Modify: `src/CoffeeShop.Api/appsettings.json`, `compose.yaml`
- Create: `tests/CoffeeShop.MessagingTests/Retry/FailureClassifierTests.cs`
- Create: `tests/CoffeeShop.MessagingTests/Retry/KafkaRetryRouterTests.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/KafkaRetryAndDeadLetterTests.cs`
- Modify: `scripts/phase-3-smoke.sh`, `tests/scripts/phase-3-smoke-tests.sh`
- Create: `docs/operations/kafka-dead-letter-replay.md`
- Create: `docs/lessons/26-retry-and-dead-letter.md`

**Interfaces:**

```csharp
public enum IntegrationFailureKind { Transient, Permanent }

public sealed record IntegrationFailure(
    IntegrationFailureKind Kind,
    string SafeErrorCode);

public interface IIntegrationFailureClassifier
{
    IntegrationFailure Classify(Exception exception);
}

internal interface IRetryDelay
{
    Task DelayUntilAsync(
        DateTimeOffset notBeforeUtc,
        CancellationToken cancellationToken);
}
```

Routing stages are original, `.retry.1` with one-second not-before, `.retry.2` with five-second not-before, then `.dlt`. Permanent failures go directly to `.dlt`.

- [ ] **Step 1: Write failing deterministic routing tests**

Use a recording `IRetryDelay` fake backed by a controlled `TimeProvider`. Assert transient failure routes original → retry 1 → retry 2 → DLT, permanent validation/version failure routes directly to DLT, cancellation routes nowhere, and successful forwarding occurs before the source offset commit. Assert DLT metadata has no stack trace, connection string, credential, or exception message.

- [ ] **Step 2: Prove the focused tests are red**

```bash
dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj --filter FullyQualifiedName~Retry
dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj --filter FullyQualifiedName~KafkaRetryAndDeadLetterTests
```

- [ ] **Step 3: Implement bounded routing and offset discipline**

Attach `delivery-attempt`, `not-before`, original topic/partition/offset, failure category/code, and failure time headers. Preserve original key, bytes, content type, envelope identity, correlation, causation, and trace headers. Commit the consumed offset only after business transaction success, duplicate no-op, or acknowledged retry/DLT publish. Rethrow `OperationCanceledException` without forwarding or committing.

- [ ] **Step 4: Add retry consumers and safe DLT operations guidance**

Retry consumers use the same handler surface and honor not-before via injected time/delay. The operations document gives inspect, classify, fix, replay-with-original-MessageId, and audit steps; it must explicitly warn that replay remains at-least-once and that DLT records can contain controlled business payload.

- [ ] **Step 5: Run real-broker proofs and full gates**

Prove transient recovery, exhausted retry, direct permanent DLT, forwarding failure/redelivery, and cancellation. Run Phase 3 smoke tests and the Shared Green Gate with no production-duration sleeps in automated tests.

- [ ] **Step 6: Document, commit, and push Lesson 26**

```bash
git add .
git commit -m "lesson(26): handle poison messages with retry and dead letters" \
  -m "Purpose: Bound consumer retries, distinguish transient and permanent failures, and preserve failed records for safe operations." \
  -m "Verification: Fake-time routing, offset-ordering, forwarding-failure, real Kafka retry/DLT, smoke, and full repository gates pass." \
  -m "Knowledge: Covers poison messages, retry budgets, delay topics, DLT metadata, cancellation, replay, and offset discipline."
git push origin learning/dotnet10-rebuild
```

Verify local/remote hashes match.

---

### Task 27: Propagate correlation and causation across HTTP and Kafka

**Files:**

- Create: `src/CoffeeShop.Api/Correlation/CorrelationMiddleware.cs`
- Create: `src/CoffeeShop.Messaging.Abstractions/IMessageIdentityAccessor.cs`
- Create: `src/CoffeeShop.Messaging.Abstractions/MessageIdentity.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/KafkaMessageIdentityScope.cs`
- Modify all module Outbox writers to take the current identity
- Modify: `src/CoffeeShop.Messaging.Kafka/KafkaIntegrationEventPublisher.cs`
- Modify: `src/CoffeeShop.Messaging.Kafka/KafkaConsumerWorker.cs`
- Modify: `src/CoffeeShop.Api/Program.cs`, structured logging tests, SignalR message mapping
- Create: `tests/CoffeeShop.ApiTests/CorrelationTests.cs`
- Create: `tests/CoffeeShop.MessagingTests/Correlation/CausationTests.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/CorrelationContinuityTests.cs`
- Create: `docs/lessons/27-correlation-and-causation.md`

**Interfaces:**

```csharp
public sealed record MessageIdentity(
    string CorrelationId,
    string? CausationId,
    string? TraceParent,
    string? TraceState);

public interface IMessageIdentityAccessor
{
    MessageIdentity Current { get; }
    IDisposable Push(MessageIdentity identity);
}
```

- [ ] **Step 1: Write failing identity-continuity tests**

Assert a request receives a server-owned correlation ID, `OrderPlacedV1` has null causation, each prepared event inherits correlation and sets causation to the consumed OrderPlaced message ID, and SignalR/log context exposes identifiers without payload/customer data. Reject malformed or oversized inbound correlation headers rather than trusting them as identity.

- [ ] **Step 2: Verify red state**

Run the three correlation-focused test classes and confirm IDs are currently generated independently or absent.

- [ ] **Step 3: Implement scoped identity at composition boundaries**

Middleware establishes root identity and response header. Consumer extraction pushes an async-local scope for the handler lifetime and always disposes it in `finally`. Outbox writers snapshot immutable identity into their own rows; they never read ambient context during later publication.

- [ ] **Step 4: Map identity to headers/logs and preserve message semantics**

Publish envelope/header identity consistently and classify mismatches as permanent contract errors. A retry keeps the original `MessageId`; a new business event receives a new `MessageId`, keeps correlation, and cites the inbound message as causation.

- [ ] **Step 5: Run end-to-end continuity and regression gates**

Run focused tests, fresh Phase 3 smoke, structured logging/SignalR tests, then the Shared Green Gate. Search captured logs to prove no payload or loyalty ID is present.

- [ ] **Step 6: Document, commit, and push Lesson 27**

```bash
git add .
git commit -m "lesson(27): correlate HTTP and Kafka workflows" \
  -m "Purpose: Carry stable business correlation and direct causation through requests, Outbox rows, Kafka headers, consumers, logs, and notifications." \
  -m "Verification: HTTP-to-Kafka continuity, retry identity, log redaction, SignalR regression, smoke, and full repository gates pass." \
  -m "Knowledge: Distinguishes workflow correlation, event causation, message identity, and W3C tracing while avoiding ambient-context leaks."
git push origin learning/dotnet10-rebuild
```

Verify local/remote hashes match.

---

### Task 28: Add backward-compatible Avro and Schema Registry governance

**Files:**

- Create: `src/CoffeeShop.Messaging.Kafka/Avro/OrderPlacedV1.avsc`
- Create: `src/CoffeeShop.Messaging.Kafka/Avro/OrderItemPreparedV1.avsc`
- Create generated-record configuration in `CoffeeShop.Messaging.Kafka.csproj`
- Create: `src/CoffeeShop.Messaging.Kafka/Avro/AvroIntegrationEventCodec.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/Avro/DualFormatIntegrationEventCodec.cs`
- Create: `src/CoffeeShop.Messaging.Kafka/Avro/AvroContractMapper.cs`
- Modify: `src/CoffeeShop.Messaging.Kafka/KafkaMessagingOptions.cs`
- Modify: `src/CoffeeShop.Messaging.Kafka/KafkaIntegrationEventPublisher.cs`
- Modify: `Directory.Packages.props`, `compose.yaml`, API configuration/readiness
- Create: `src/CoffeeShop.Api/Health/SchemaRegistryReadinessHealthCheck.cs`
- Create: `tests/CoffeeShop.MessagingTests/Avro/AvroMappingTests.cs`
- Create: `tests/CoffeeShop.MessagingTests/Fixtures/order-placed-v1-breaking.avsc`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/SchemaCompatibilityTests.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/DualFormatKafkaTests.cs`
- Modify: Phase 3 smoke scripts
- Create: `docs/lessons/28-avro-schema-evolution.md`

**Interfaces:**

The dual reader selects `application/json` or `application/avro` from the required content-type header. Outbox canonical JSON remains unchanged. Producer format is an enum option `Json` or `Avro`; after reader compatibility is proven, production/default Compose selects `Avro`.

- [ ] **Step 1: Write failing mapping, dual-reader, and compatibility tests**

Assert the old checked-in JSON V1 fixture still decodes, Avro round-trips both V1 payloads, unknown content type is permanent failure, an additive schema with a default is backward-compatible, and the checked-in breaking schema is rejected with an assertion naming the incompatible field.

- [ ] **Step 2: Verify the tests are red**

Run Avro-focused messaging tests and Schema Registry integration tests; expect missing codecs/schema service.

- [ ] **Step 3: Add pinned Confluent schema packages and generated types**

Pin `Confluent.SchemaRegistry` and `Confluent.SchemaRegistry.Serdes.Avro` `2.15.0`. Keep generated Avro records internal to `CoffeeShop.Messaging.Kafka`; map them explicitly to broker-neutral contracts. Configure Record Name Strategy and registry compatibility `BACKWARD`; every additive schema field must have a default.

- [ ] **Step 4: Roll out reader first, writer second**

First register dual decoding while JSON remains producer format and run the full suite. Then switch the configured producer/default Compose to Avro and run again. Retry/DLT topics reuse the record subject; do not bind subjects to retry topic names.

- [ ] **Step 5: Add Schema Registry Compose/readiness and run full gates**

Use `confluentinc/cp-schema-registry:8.1.0`, backed by the default Kafka service. Readiness checks it only when Avro production is selected. Run real JSON+Avro broker tests, compatibility tests, fresh Phase 3 smoke, and the Shared Green Gate with the `schema` profile included.

- [ ] **Step 6: Document, commit, and push Lesson 28**

```bash
git add .
git commit -m "lesson(28): govern event schemas with Avro" \
  -m "Purpose: Introduce schema-first Avro contracts through a reader-first rollout while retaining the Version 1 JSON compatibility window." \
  -m "Verification: JSON/Avro dual-read, real Schema Registry compatibility, breaking-fixture rejection, smoke, and all repository gates pass." \
  -m "Knowledge: Covers schema ownership, backward compatibility, defaults, subject strategies, format/version separation, and safe rollout order."
git push origin learning/dotnet10-rebuild
```

Verify local/remote hashes match.

---

### Task 29: Trace and measure distributed order processing

**Files:**

- Create: `src/CoffeeShop.Messaging.Abstractions/MessagingTelemetry.cs`
- Create: `src/CoffeeShop.Api/Telemetry/OpenTelemetryExtensions.cs`
- Modify Kafka publisher/consumer, all Outbox workers/stores, and all Inbox handlers to emit bounded activities/metrics
- Modify: `src/CoffeeShop.Api/Program.cs`, `src/CoffeeShop.Api/appsettings.json`, `Directory.Packages.props`
- Modify: `compose.yaml`
- Create: `deploy/otel-collector/config.yaml`
- Create: `tests/CoffeeShop.MessagingTests/Telemetry/MessagingActivityTests.cs`
- Create: `tests/CoffeeShop.MessagingTests/Telemetry/MessagingMetricTests.cs`
- Create: `tests/CoffeeShop.ApiTests/OpenTelemetryConfigurationTests.cs`
- Modify: Phase 3 smoke scripts
- Create: `docs/lessons/29-opentelemetry.md`

**Interfaces:**

`MessagingTelemetry` owns one `ActivitySource` and one `Meter`. Instruments include publish/consume duration, pending Outbox, publish attempts, Inbox duplicates, retries, and dead letters. Tags are restricted to event type, module, topic/destination, operation, result, and retry level.

- [ ] **Step 1: Write failing ActivityListener/MeterListener tests**

Assert HTTP parent → persisted Outbox context → producer span → consumer span → business/next-Outbox span relationships. Assert required metrics appear and reject any tag key/value containing order, message, correlation, loyalty, payload, or exception text.

- [ ] **Step 2: Verify red state**

Run telemetry-focused unit/API tests; expect missing sources, meters, and registration.

- [ ] **Step 3: Add current OpenTelemetry packages and registration**

Pin `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, and `OpenTelemetry.Instrumentation.AspNetCore` `1.18.0`. Pin the current prerelease `OpenTelemetry.Instrumentation.EntityFrameworkCore` `1.18.0-beta.1` explicitly because no stable package is published; record this exception in the lesson. Use `AddOpenTelemetry().WithTracing(...).WithMetrics(...)`; add OTLP export only when a validated endpoint exists.

- [ ] **Step 4: Instrument without changing business semantics**

Persist W3C `traceparent`/`tracestate` at Outbox creation, inject headers on publish, extract before consumer activity creation, and do not use business IDs as metric dimensions. Logs may include safe message/correlation IDs but never payloads.

- [ ] **Step 5: Add optional observability profile and prove it**

Add pinned OpenTelemetry Collector Contrib and Jaeger v2 services under `observability`. Collector receives OTLP, exports traces to Jaeger, and exposes Prometheus-format metrics. Do not add Grafana or a Prometheus server. Run listener tests without containers, then a Compose smoke that observes Collector/Jaeger health and exported telemetry.

- [ ] **Step 6: Run full gates, document, commit, and push Lesson 29**

```bash
git add .
git commit -m "lesson(29): observe distributed order processing" \
  -m "Purpose: Continue W3C traces and emit low-cardinality metrics across HTTP, EF, Outbox, Kafka, Inbox, cache, and business work." \
  -m "Verification: Activity/Meter listener assertions, redaction/cardinality checks, OTLP Compose smoke, and full repository gates pass." \
  -m "Knowledge: Covers asynchronous trace parenting, instrumentation boundaries, metric cardinality, conditional exporters, and actionable telemetry."
git push origin learning/dotnet10-rebuild
```

Verify local/remote hashes match.

---

### Task 30: Provide an optional Dapr pub/sub adapter

**Files:**

- Create: `src/CoffeeShop.Messaging.Dapr/CoffeeShop.Messaging.Dapr.csproj`
- Create: `src/CoffeeShop.Messaging.Dapr/DaprMessagingOptions.cs`
- Create: `src/CoffeeShop.Messaging.Dapr/DaprIntegrationEventPublisher.cs`
- Create: `src/CoffeeShop.Messaging.Dapr/DaprSubscriptionEndpoints.cs`
- Create: `src/CoffeeShop.Messaging.Dapr/DaprServiceCollectionExtensions.cs`
- Modify: `src/CoffeeShop.Api/CoffeeShop.Api.csproj`, `Program.cs`, configuration and readiness
- Create: `src/CoffeeShop.Api/Health/DaprReadinessHealthCheck.cs`
- Modify: `Directory.Packages.props`, `CoffeeShop.slnx`, `compose.yaml`
- Create: `deploy/dapr/components/pubsub.yaml`
- Create: `tests/CoffeeShop.MessagingTests/Adapters/MessagingAdapterContractTests.cs`
- Create: `tests/CoffeeShop.Messaging.IntegrationTests/DaprAdapterTests.cs`
- Modify: architecture tests and Phase 3 smoke scripts
- Create: `docs/checkpoints/phase-3.md`
- Create: `docs/lessons/30-dapr-pubsub-adapter.md`

**Interfaces:**

Adapter selection is validated enum `Kafka` or `Dapr`, default `Kafka`. Both implement `IIntegrationEventPublisher` and invoke the same `IIntegrationEventHandler<T>` registrations. Dapr state/workflow APIs and Dapr types never enter contracts or modules.

- [ ] **Step 1: Write failing shared adapter contract tests**

Run the same suite against Kafka and a Dapr publisher/receiver test double: topic mapping, key/envelope/header identity, duplicate delivery through module Inbox, permanent failure response, and cancellation. Add architecture assertions that only API and Messaging.Dapr reference Dapr packages.

- [ ] **Step 2: Verify red state**

Run adapter contract and architecture tests; expect missing Dapr project/registration.

- [ ] **Step 3: Add pinned Dapr packages and thin adapter**

Pin `Dapr.Client` and `Dapr.AspNetCore` `1.18.5`. Publish with `DaprClient.PublishEventAsync`; expose only adapter-owned HTTP subscription endpoints and `MapSubscribeHandler`. Translate CloudEvent/Dapr delivery data to the existing envelope/context, then invoke the existing handlers. Keep Kafka retry/DLT as the reference implementation and document Dapr runtime retry differences.

- [ ] **Step 4: Add optional sidecar profile and readiness**

Add only the application Dapr sidecar configuration under profile `dapr`, backed by the existing Kafka pub/sub component. Do not add the placement service because this phase uses neither actors nor workflows. Selecting Dapr requires the sidecar health endpoint; default startup still selects Kafka and does not require Dapr.

- [ ] **Step 5: Run both adapter proofs and Phase 3 acceptance gate**

Run shared contract tests against both surfaces, fresh Kafka-default smoke, optional Dapr smoke, architecture tests, identity/Redis fault checks, Schema Registry/observability profiles, frontend build, all Compose image builds, and the Shared Green Gate. Verify exactly ten lesson commits exist after Lesson 20 and every commit has one Vietnamese lesson document.

- [ ] **Step 6: Write checkpoint/lesson docs, commit, push, and verify Phase 3**

```bash
git add .
git commit -m "lesson(30): provide a Dapr pubsub adapter" \
  -m "Purpose: Demonstrate transport substitution with an optional Dapr pub/sub adapter while Kafka remains the default reliable path." \
  -m "Verification: Shared Kafka/Dapr contract tests, architecture rules, optional sidecar smoke, Phase 3 acceptance matrix, and full gates pass." \
  -m "Knowledge: Covers sidecar trade-offs, adapter boundaries, delivery-semantic differences, optional infrastructure, and framework containment."
git push origin learning/dotnet10-rebuild
git rev-parse HEAD
git rev-parse origin/learning/dotnet10-rebuild
git log --oneline 2aa52fd..HEAD
```

Expected: local and remote hashes match; log contains exactly Lessons 21–30 in order. Create a Phase 3 annotated tag only if strict restore/audit succeeds for every published Phase 3 commit without history rewriting.

---

## Plan Self-Review Checklist

- [x] Every acceptance criterion and non-goal in the Phase 3 design spec maps to a task above.
- [x] No task claims exactly-once delivery or introduces a distributed PostgreSQL/Kafka transaction.
- [x] Lesson 25 is the only business-workflow cutover and never runs old/new preparation side effects together.
- [x] Contract signatures, event names, topic names, retry suffixes, handler signatures, and adapter selection names are consistent across tasks.
- [x] JSON V1 remains readable after the Lesson 28 producer switch.
- [x] Kafka stays default in Lesson 30 and Phase 4 remains responsible for process extraction.
- [x] No `TBD`, `TODO`, “implement later”, unspecified error handling, or undefined neighboring interface remains.
