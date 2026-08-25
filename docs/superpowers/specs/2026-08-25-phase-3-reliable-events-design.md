# Phase 3 Reliable Event-Driven Integration Design

## 1. Purpose

Phase 3 replaces the cross-module, in-process preparation workflow with reliable Kafka integration while keeping the CoffeeShop application a single deployable modular monolith. It teaches versioned integration contracts, Kafka transport, Transactional Outbox, idempotent Inbox handling, bounded retries, dead-letter routing, schema evolution, and distributed observability before Phase 4 extracts any worker process.

Kafka becomes the real business workflow in Lesson 25. Lessons 22–24 introduce it safely as transport and shadow publication first. The phase guarantees at-least-once delivery with idempotent business handling; it does not claim end-to-end exactly-once delivery.

This design is the Phase 3 child specification for the approved .NET 10 curriculum master plan. It elaborates Lessons 21–30 without changing their order, commit subjects, or learning goals.

## 2. Constraints Carried Forward

- Target .NET 10 and keep centrally pinned package versions.
- Preserve the observable `/v1`, `/v2`, SignalR, authorization, caching, and operational behavior delivered by Phases 1 and 2.
- Keep code identifiers and commit messages in English; keep lesson documents in Vietnamese.
- Every lesson is one coherent commit that restores, builds, tests, and passes its applicable frontend/container checks independently.
- Push each lesson immediately after its green verification gate, as explicitly approved after the master plan was written.
- Keep domain and application policy independent from ASP.NET Core, EF Core, Kafka, Dapr, Schema Registry, Redis, and telemetry implementations.
- Use in-process domain events only for effects within the deployable process. Use versioned integration events for cross-module workflows intended for later process extraction.
- Use PostgreSQL-backed integration tests for Outbox/Inbox behavior and broker-backed integration tests for Kafka behavior.
- Keep secrets and customer data out of source control, integration events, logs, metric labels, and failure metadata.
- Do not rewrite already published lesson history to accommodate later package-audit changes.

## 3. Approaches Considered

### 3.1 Shadow Kafka for the entire phase

Publish copies of domain outcomes to Kafka but keep Barista, Kitchen, and Counter connected only through the existing in-process dispatcher.

This is operationally safe but does not teach the failure modes that Inbox idempotency, retry topics, offset discipline, and trace propagation are meant to solve. Rejected.

### 3.2 Extract workers as soon as Kafka appears

Create separate Barista and Kitchen processes in Lesson 22 and teach transport and service decomposition together.

This introduces deployment, database ownership, migrations, networking, and service lifecycle at the same time as reliable messaging. It weakens the lesson boundaries and duplicates Phase 4. Rejected.

### 3.3 Evolve a broker-backed modular monolith — selected

Add broker-neutral contracts and ports, implement Kafka in the API host, introduce Outbox and Inbox inside the owning modules, and switch the real workflow only after the reliability mechanisms are present. Keep one deployable process through Lesson 30.

This preserves the Phase 2 module seams, creates a controlled cutover, and leaves Phase 4 with extraction rather than redesign work.

## 4. Target Project Topology

```text
CoffeeShop.Api
├── CoffeeShop.Modules.Counter
├── CoffeeShop.Modules.Barista
├── CoffeeShop.Modules.Kitchen
├── CoffeeShop.Messaging.Kafka
├── CoffeeShop.Messaging.Dapr          (Lesson 30, optional)
├── CoffeeShop.Messaging.Abstractions
├── CoffeeShop.IntegrationContracts
├── CoffeeShop.Contracts
└── CoffeeShop.SharedKernel

CoffeeShop.Modules.* ──► CoffeeShop.IntegrationContracts
CoffeeShop.Modules.* ──► CoffeeShop.Messaging.Abstractions
CoffeeShop.Messaging.Kafka ──► CoffeeShop.Messaging.Abstractions
CoffeeShop.Messaging.Kafka ──► CoffeeShop.IntegrationContracts
CoffeeShop.Messaging.Dapr ──► CoffeeShop.Messaging.Abstractions
CoffeeShop.Messaging.Dapr ──► CoffeeShop.IntegrationContracts
CoffeeShop.Api ──► modules and selected transport adapters
```

`CoffeeShop.IntegrationContracts` is framework- and broker-free. It owns the public envelope and event payloads.

`CoffeeShop.Messaging.Abstractions` contains small transport-facing ports and metadata types. It depends only on the BCL and IntegrationContracts.

`CoffeeShop.Messaging.Kafka` owns Confluent producer/consumer code, topic mapping, codecs, hosted Kafka consumer poll loops, retry/DLT forwarding, and messaging telemetry. It never accesses module databases or applies business policy.

The Kafka codecs map the broker-neutral contracts to JSON or generated Avro wire records. Avro-generated types and Schema Registry serializers never cross the adapter boundary.

`CoffeeShop.Messaging.Dapr` implements the same application-facing ports in Lesson 30. Dapr APIs do not enter the modules, contracts, or domain.

The API host remains the composition root. It selects the adapter, registers consumers, and owns host lifecycle. No business module references another module or a concrete messaging adapter.

## 5. Integration Contracts

Domain events and integration events remain distinct types. Phase 2 contracts such as `OrderItemAccepted` and `OrderItemPrepared` describe in-process domain outcomes. Phase 3 maps those outcomes explicitly to stable public messages.

The broker-neutral envelope is:

```csharp
public sealed record IntegrationEventEnvelope<TPayload>(
    Guid MessageId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    TPayload Payload);
```

Version 1 payloads are:

```csharp
public sealed record OrderPlacedV1(
    Guid OrderId,
    IReadOnlyList<OrderLineItemV1> Items);

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
    DateTimeOffset OccurredAtUtc);
```

The payload keeps the current behavior-critical line-item identity, maker, and completion time. It deliberately omits loyalty member ID, location, order source, price, and other data that Barista or Kitchen do not need.

Stable semantic names are:

- `coffeeshop.order-placed`, version `1`.
- `coffeeshop.order-item-prepared`, version `1`.

CLR type names are not used as wire identifiers. A new compatible field does not automatically require a new topic or a new event version. A semantic breaking change requires a new event version and an explicit migration plan.

## 6. Topics, Keys, and Headers

The initial topology is:

```text
coffeeshop.orders.v1
coffeeshop.preparation.v1
```

`OrderId` in canonical UUID form is the Kafka key for both topics. This keeps all preparation results for one order in a stable partition order without making global ordering claims.

The `v1` topic suffix identifies this topology generation. Event contract versions remain explicit in the envelope and schema; compatible event evolution does not create a topic per CLR type.

Transport metadata uses lowercase ASCII headers:

- `message-id`
- `event-type`
- `event-version`
- `occurred-at`
- `correlation-id`
- `causation-id`
- `content-type`
- `traceparent`
- `tracestate`
- retry/DLT metadata when applicable

Business metadata remains present in the envelope even when duplicated in headers. Headers allow routing, tracing, and diagnostics without deserializing the payload. A mismatch between required envelope and header identity is a permanent contract failure.

## 7. Messaging Ports and Dispatch

The application-facing surface supports:

- Publishing a fully described outbound integration message.
- Handling a typed integration envelope.
- Accessing immutable message metadata for correlation, causation, and delivery attempt.
- Classifying failures without exposing Kafka exception types to modules.

Codec selection, Kafka records, partitions, offsets, consumer groups, poll loops, and Dapr request types stay behind the adapters. The host maps an event type/version pair to exactly one registered handler per consumer role.

Each module owns an internal Outbox store and polling worker registered through that module's composition method. The worker claims rows through the owning module's `DbContext` and calls the broker-neutral publisher port. The concrete Kafka publisher only encodes and sends the supplied logical message. This keeps the database transaction and lease policy with the data owner while keeping Kafka mechanics in the adapter.

Consumer roles are separate even while they run in one process:

- Barista group consumes `OrderPlacedV1` and processes Barista items.
- Kitchen group consumes `OrderPlacedV1` and processes Kitchen items.
- Counter group consumes `OrderItemPreparedV1` and completes line items.

Each role receives the same order event through a distinct consumer group. A role with no matching items records successful Inbox handling with no business side effect.

## 8. Module-Owned Persistence

Each module owns its reliability tables in its existing PostgreSQL schema:

```text
counter.outbox_messages
counter.inbox_messages
barista.outbox_messages
barista.inbox_messages
kitchen.outbox_messages
kitchen.inbox_messages
```

Each module owns its EF mapping and migrations. The Kafka adapter cannot query or mutate these tables. Module application code owns the local transaction that combines business state with its Outbox or Inbox state.

An Outbox record contains at least:

- message identity, event type/version, canonical contract JSON, and occurrence time;
- correlation and causation identity;
- persisted W3C trace parent/state;
- attempt count and next-attempt time;
- lease ID and lease expiry;
- published time;
- a bounded safe error code.

An Inbox record contains at least:

- handler identity and message identity as a unique key;
- received and processed times;
- event type/version;
- a bounded processing result.

Inbox tables do not become an event archive. They retain only the information needed for deduplication and operations.

Canonical JSON is an internal Outbox persistence format, not a promise about the Kafka wire format. It lets a module save an integration contract without depending on Kafka, Avro, or Schema Registry. At publish time the selected adapter reads the logical contract and encodes it as the configured JSON or Avro wire record. The adapter writes the matching `content-type` header. Existing unpublished Outbox rows therefore remain publishable across the Lesson 28 producer-format switch.

## 9. Transactional Outbox Semantics

The Counter transaction that accepts an order also writes one `OrderPlacedV1` Outbox record. Barista and Kitchen transactions that prepare items write `OrderItemPreparedV1` records in the same local transaction as their preparation state.

The publisher claims work as follows:

1. Start a short PostgreSQL transaction.
2. Select a bounded due batch with `FOR UPDATE SKIP LOCKED`.
3. Assign a lease ID and expiry, then commit.
4. Publish outside the database transaction.
5. Mark a record published only after Kafka acknowledges it, conditioned on the publisher still owning the lease.
6. On failure, record a safe error code and a bounded next-attempt time.

The publisher never holds a database lock across Kafka network I/O. Multiple publisher instances may poll safely. An expired lease is reclaimable.

If a process dies after Kafka acknowledges a publish but before `PublishedAtUtc` is saved, the record is published again after lease expiry. This duplicate window is intentional and is closed by Inbox idempotency, not by an exactly-once claim.

Outbox transport retry is distinct from consumer-processing retry topics. The former retries delivery from the database to Kafka; the latter handles a record that Kafka delivered but the consumer could not process.

## 10. Inbox and Offset Semantics

Every consumer handler executes one local PostgreSQL transaction:

1. Insert `(HandlerName, MessageId)` into the owning Inbox.
2. If the unique key already exists, perform no business side effect and treat delivery as successful.
3. If it is new, mutate owned business state and write any resulting Outbox records.
4. Mark Inbox processing complete and commit the transaction.
5. Commit the Kafka offset only after the database commit succeeds.

If the process stops after the database commit but before the offset commit, Kafka redelivers the record and the Inbox converts it to a no-op. If business handling fails, the local transaction rolls back, including the Inbox insert and any outgoing event.

Kafka auto-commit is disabled. Consumer cancellation stops polling, allows bounded in-flight cleanup, and closes the consumer so partition ownership returns cleanly to the group.

Producer idempotence is enabled where supported to reduce broker-level duplicates, but business correctness never depends on it.

## 11. Workflow Introduction and Cutover

The transition is intentionally staged:

### Lesson 21

Introduce contracts and compatibility tests. Runtime behavior does not change.

### Lesson 22

Introduce Kafka JSON transport and a real-broker round-trip. No business consumer changes state.

### Lesson 23

Counter writes `OrderPlacedV1` to its Outbox in the same order transaction. Existing in-process preparation handlers remain active, so behavior is unchanged.

### Lesson 24

The Outbox publisher sends shadow records to Kafka. Kafka consumers still have no business side effect.

### Lesson 25

Perform one atomic source/configuration cutover:

- Remove Barista and Kitchen cross-module `OrderItemAccepted` handlers from in-process composition.
- Register Barista and Kitchen business consumers for `OrderPlacedV1`.
- Map prepared outcomes to module Outbox records instead of sending them cross-module in process.
- Register the Counter consumer for `OrderItemPreparedV1`.
- Retain local in-process effects such as Counter order-update notifications, SignalR publication, and cache invalidation.

No released commit runs both the old and new preparation side effect for the same item. The HTTP contracts remain unchanged. Fulfilment becomes eventually consistent through Kafka, and existing bounded eventual assertions continue to prove completion.

## 12. Retry and Dead-Letter Semantics

Lesson 26 adds bounded consumer-processing retries:

```text
original delivery
  -> transient failure -> {topic}.retry.1 after 1 second
  -> transient failure -> {topic}.retry.2 after 5 seconds
  -> transient failure -> {topic}.dlt

permanent contract or validation failure
  -> {topic}.dlt immediately
```

The configured retry budget has three logical stages: original, retry 1, and retry 2. At-least-once redelivery can repeat a stage if a process stops in its publish/offset crash window, so the system does not claim that a handler invocation can occur only three physical times. Inbox idempotency protects successful business effects, and DLT operations deduplicate by the original message ID.

Retry records add a bounded processing-attempt value and a not-before time. Dedicated retry consumers honor the not-before time through an injected clock/delay before dispatch. Production waits are shorter than the configured consumer poll interval; automated tests advance fake time and never sleep for the production backoff.

Transient failures include bounded infrastructure unavailability and retryable concurrency/database failures. Permanent failures include unsupported event versions, invalid required fields, unknown contract values, and domain-invalid messages. `OperationCanceledException` is lifecycle control: it is neither retried nor dead-lettered, and its offset is not committed.

A retry or DLT publish must succeed before the source offset is committed. If forwarding fails, the source record remains uncommitted and is delivered again.

Retry records preserve the original key, envelope, `MessageId`, correlation, causation, content type, and trace context. Business events created by successful handling receive a new `MessageId`, inherit the workflow correlation ID, and set `CausationId` to the inbound message ID.

Moving a record through retry topics does not preserve strict ordering relative to later records on the source topic. The CoffeeShop handlers therefore remain idempotent and tolerant of line-item completion order; no phase requirement depends on ordering across retry topics.

A DLT record preserves the original record/envelope plus source topic/partition/offset, attempt, failure category, safe error code, and failure time. It never contains exception stack traces, connection data, credentials, or tokens. Records that cannot be deserialized preserve the original bytes and safe transport metadata for controlled inspection/replay.

## 13. JSON and Avro Evolution

Lessons 21–27 use JSON with:

- camel-case property names;
- canonical string UUIDs;
- ISO-8601 UTC timestamps;
- validation of required fields and known semantic values;
- tolerance for unknown additive fields;
- checked-in golden Version 1 fixtures.

Lesson 28 performs a reader-first rollout:

1. Add a dual reader selected by `content-type` for JSON and Avro.
2. Prove old JSON Version 1 fixtures and new Avro records are both accepted.
3. Configure Schema Registry compatibility as `BACKWARD`.
4. Use Record Name Strategy so original, retry, and DLT topic movement does not create incompatible topic-bound subjects.
5. Require defaults for newly added Avro fields.
6. Switch the producer to Avro only after the dual reader is green.

The Kafka adapter maps generated Avro records to and from the broker-neutral IntegrationContracts types. Generated schema types never enter a module. The old JSON reader and fixture remain after the producer switch. Serialization format, Outbox storage format, and business event version are separate decisions. A producer rollback to JSON remains safe during the compatibility window.

Evolution tests accept a backward-compatible additive schema and reject a breaking fixture with an explanatory assertion.

## 14. Correlation, Tracing, Metrics, and Logs

Lesson 27 makes business identity explicit:

- A new order workflow receives a server-generated correlation ID.
- The root `OrderPlacedV1` has no causation ID.
- Resulting `OrderItemPreparedV1` messages keep the correlation ID and cite the consumed message ID as causation.
- Correlation and causation flow through Outbox rows, envelope fields, Kafka headers, and structured logs.

Business correlation is distinct from W3C trace identity. Outbox rows persist `traceparent` and optional `tracestate`; publishers inject them into Kafka headers; consumers extract them before starting consumer activities.

Lesson 29 instruments HTTP, EF Core, Kafka, Outbox, Inbox, cache, and business processing with OpenTelemetry. OTLP export is enabled only when an endpoint is configured. The optional `observability` Compose profile contains an OpenTelemetry Collector and Jaeger. Jaeger displays traces; the Collector exposes a Prometheus-format metrics endpoint. Grafana and a Prometheus server are outside this phase.

Metrics cover bounded dimensions such as event type, operation, destination, and result:

- published and consumed message counts;
- processing duration;
- pending Outbox records and publish failures;
- Inbox duplicates;
- retry forwarding;
- DLT forwarding.

`OrderId`, `MessageId`, and `CorrelationId` may appear in trace spans and safe structured logs but never as metric labels.

Logs include event type/version, topic/partition, message/correlation/causation IDs, attempt, and safe error code. Logs never include event payloads, loyalty identity, authorization data, secrets, or broker credentials.

## 15. Health and Runtime Configuration

Liveness remains process-only. Readiness includes Kafka when Kafka messaging is enabled, Schema Registry when Avro production is enabled, and the Dapr sidecar when the Dapr adapter is selected.

Kafka loss makes readiness unhealthy while liveness remains healthy. The PostgreSQL Outbox still prevents accepted orders from being silently lost, although an orchestrator may remove an unready instance from service.

All topic names, consumer groups, batch sizes, lease durations, poll intervals, retry delays, and adapter selection use validated options with safe defaults for the learning environment. Credentials and deployment-specific endpoints come from environment/configuration and are never committed.

## 16. Optional Dapr Adapter

Lesson 30 adds Dapr pub/sub behind the existing application-facing messaging ports:

- Kafka remains the default path and the reference reliability implementation.
- Dapr is selected explicitly by configuration and an optional Compose profile.
- The same application contracts and handler surfaces are used.
- Adapter contract tests run against Kafka and Dapr surfaces.
- Module code contains no Dapr API or attribute.
- Dapr state stores and workflow APIs are outside scope.

The adapter does not erase delivery differences between transports. Inbox idempotency remains mandatory, and the lesson documents sidecar lifecycle, delivery, retry, and operational trade-offs.

## 17. Testing Strategy

### 17.1 Unit and contract tests

- Protect envelope identity, semantic event names, required fields, JSON shape, and golden fixtures.
- Test metadata propagation and failure classification without Kafka.
- Test retry scheduling with fake time/delay.
- Test mappings from domain outcomes to integration payloads.

### 17.2 Architecture tests

- IntegrationContracts remains framework- and broker-free.
- Messaging.Abstractions depends only on approved contracts/BCL types.
- Modules do not reference Kafka, Dapr, or Schema Registry packages.
- Concrete adapters do not access module persistence implementations.

### 17.3 PostgreSQL integration tests

- Prove business state and Outbox commit or roll back together.
- Prove Inbox, business side effect, and resulting Outbox commit or roll back together.
- Exercise lease competition, expiry, reclaim, duplicate delivery, and optimistic concurrency.

### 17.4 Kafka and Schema Registry integration tests

Use Testcontainers with xUnit lifecycle fixtures, random host ports, run-specific topic names, and run-specific consumer groups. Do not depend on a developer-installed broker or fixed host port.

- Round-trip JSON and Avro through real Kafka.
- Prove manual offset behavior and clean shutdown.
- Prove duplicate delivery creates one business side effect.
- Prove retry and DLT routing.
- Prove compatible schema evolution and reject a breaking fixture.

Containers may be shared at collection scope for speed, but test database/schema, topics, and consumer groups remain isolated. Resource cleanup is deterministic.

### 17.5 Observability tests

Use `ActivityListener` and `MeterListener` to assert parent/child relationships, propagation, and bounded metric dimensions. Automated tests do not require Jaeger, the Collector UI, or arbitrary sleep.

### 17.6 Compose smoke tests

Run from fresh volumes with bounded eventual assertions. Prove authenticated order placement, Kafka-driven preparation, Counter completion, SignalR/cache behavior, Kafka readiness failure/recovery, Avro/Schema Registry operation, and optional observability/Dapr profiles as their lessons appear.

## 18. Compose Evolution

- Lessons 22–24 place Kafka under a `messaging` profile because it carries only test/shadow traffic.
- Lesson 25 makes Kafka part of the default Compose workflow.
- Lesson 28 makes Schema Registry part of the default Avro workflow.
- Lesson 29 adds the optional `observability` profile.
- Lesson 30 adds the optional `dapr` profile; Kafka remains default.

Every lesson validates Compose configuration and builds all newly affected images. Phase smoke tests tear down volumes and orphan containers so a previous run cannot hide migration or configuration errors.

## 19. Lesson Sequence and Commit Contracts

1. Lesson 21 — `lesson(21): define versioned integration events`
2. Lesson 22 — `lesson(22): exchange integration events through Kafka`
3. Lesson 23 — `lesson(23): persist integration events in an outbox`
4. Lesson 24 — `lesson(24): publish pending outbox messages`
5. Lesson 25 — `lesson(25): deduplicate consumed messages with an inbox`
6. Lesson 26 — `lesson(26): handle poison messages with retry and dead letters`
7. Lesson 27 — `lesson(27): correlate HTTP and Kafka workflows`
8. Lesson 28 — `lesson(28): govern event schemas with Avro`
9. Lesson 29 — `lesson(29): observe distributed order processing`
10. Lesson 30 — `lesson(30): provide a Dapr pubsub adapter`

Each commit includes a Vietnamese lesson document with purpose, implementation narrative, verification commands/evidence, failure scenarios, and a knowledge summary. Red TDD states remain uncommitted; no published lesson commit is knowingly red.

Before each push:

1. Run strict restore with no unresolved audit warning.
2. Build Release with zero warnings and errors.
3. Run the full applicable .NET test suite.
4. Build the frontend.
5. Validate Compose and build affected images.
6. Run the lesson-specific broker/database/smoke proof from clean state.
7. Run shell/config syntax checks and `git diff --check`.
8. Confirm the commit contains exactly one lesson and its documentation.
9. Push immediately and verify the local hash equals the remote branch hash.

## 20. Non-Goals

- Extracting Barista or Kitchen into another process; that begins in Lesson 31.
- Database-per-service; Phase 3 keeps module-owned schemas in one PostgreSQL database.
- End-to-end exactly-once delivery or a distributed database/Kafka transaction.
- Debezium, CDC, Kafka Streams, event sourcing, sagas, or workflow orchestration.
- Dapr state/workflow adoption or making Dapr the default transport.
- A general-purpose internal messaging framework.
- Grafana, a Prometheus server, or a production platform deployment.
- Rewriting Phase 1 or Phase 2 history.

## 21. Phase Acceptance Criteria

Phase 3 is complete only when:

- Lessons 21–30 exist as ten ordered, independently green commits on `learning/dotnet10-rebuild`.
- Each lesson commit has its Vietnamese lesson document and exact approved English commit subject.
- The real order workflow uses Kafka from Counter through Barista/Kitchen and back to Counter.
- PostgreSQL Outbox and Inbox tests prove local atomicity and duplicate protection.
- Retry/DLT behavior is bounded, deterministic in tests, and operationally documented.
- JSON Version 1 remains readable after Avro becomes the default producer format.
- Trace/correlation continuity and low-cardinality metrics have automated proof.
- Kafka is the default adapter and Dapr remains optional without module leakage.
- Fresh-state solution, frontend, container, identity, Redis, Kafka, Schema Registry, observability, and applicable Dapr checks pass.
- Local and remote curriculum branch hashes match after every lesson push.
