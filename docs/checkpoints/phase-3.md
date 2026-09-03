# Checkpoint Phase 3 — Reliable event-driven modular monolith

Phase 3 kết thúc sau mười lesson commits 21–30. Checkpoint giữ hệ thống là một deployable modular monolith,
nhưng workflow fulfillment thật đã đi qua integration events và Kafka thay vì in-process domain handlers.

## Năng lực tích lũy

- Lesson 21–22: broker-neutral Version 1 contracts, JSON codec và Kafka transport có manual offset.
- Lesson 23–25: module-owned Transactional Outbox/Inbox, lease cạnh tranh và idempotent business effects.
- Lesson 26: bounded retry topics, DLT và replay runbook cho Kafka path.
- Lesson 27: correlation/causation liên tục qua HTTP, persisted Outbox, broker, logs và SignalR.
- Lesson 28: Avro schema-first, BACKWARD compatibility và dual-format reader rollout.
- Lesson 29: W3C distributed traces, low-cardinality metrics, optional Collector/Jaeger.
- Lesson 30: Kafka/Dapr adapter seam, optional sidecar và framework containment.

## Reliability contract tại checkpoint

```text
HTTP accepts order
  -> order + Counter Outbox commit atomically
  -> selected publisher sends OrderPlacedV1
  -> Barista/Kitchen Inbox + effect + Outbox commit locally
  -> selected publisher sends two OrderItemPreparedV1
  -> Counter Inbox + fulfillment commit locally
  -> SignalR update + Redis read-model invalidation
```

Semantics là at-least-once. Crash sau broker ACK nhưng trước `PublishedAtUtc`, hoặc sau database commit
nhưng trước delivery ACK/offset commit, có thể tạo duplicate. Inbox đóng duplicate window ở business
boundary; phase này không claim distributed transaction hay end-to-end exactly-once.

Kafka là path mặc định và sở hữu reference reliability controls: Avro/Schema Registry, bounded retry,
application DLT và replay. Dapr là opt-in path dùng cùng contracts/handlers qua Kafka component; retry/ACK
do runtime sở hữu và không có application DLT trong Lesson 30.

## Boundary được khóa

- IntegrationContracts không phụ thuộc framework, module hoặc broker.
- Messaging.Abstractions chỉ sở hữu ports, identity, failure classification, telemetry và semantic topics.
- Counter, Barista, Kitchen không phụ thuộc Kafka, Dapr hoặc nhau.
- Kafka và Dapr adapters không phụ thuộc nhau hay module persistence.
- Chỉ API composition root và `CoffeeShop.Messaging.Dapr` được dùng Dapr framework types.
- Dapr subscription discovery/callback yêu cầu app-channel token; malformed delivery được ACK `DROP`.
- Mỗi module sở hữu schema, migration, Inbox, Outbox và local transaction của mình.

## Acceptance matrix

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --configuration Release --no-restore
dotnet test CoffeeShop.slnx --configuration Release --no-build

docker run --rm -v "$PWD:/work" -w /work/src/CoffeeShop.SignalRClient node:22-alpine \
  sh -c 'npm ci && npm run build'

docker compose --profile demo --profile identity --profile observability --profile dapr config --quiet
docker compose --profile demo --profile identity --profile observability --profile dapr build
./tests/scripts/phase-3-smoke-tests.sh
```

Kafka-default fresh proof:

```bash
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka schema-registry api signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
./scripts/phase-3-smoke.sh
```

Dapr fresh proof:

```bash
docker compose --profile dapr down --volumes --remove-orphans
MESSAGING_ADAPTER=Dapr docker compose --profile dapr up -d --build \
  postgres redis kafka api dapr-sidecar
MESSAGING_ADAPTER=Dapr ./scripts/phase-3-smoke.sh
docker compose --profile dapr down --volumes --remove-orphans
```

CI còn chạy identity, DataGen và observability profiles độc lập từ fresh volumes. Mỗi smoke có global
deadline và in diagnostics khi failure, nên dependency mất không biến thành wait vô hạn.

## Audit lịch sử học

```bash
git log --reverse --format='%h %s' --grep='^lesson(' 2aa52fd..HEAD
find docs/lessons -maxdepth 1 -type f -name '*.md' | sort
git diff --check HEAD^
```

Kết quả mong đợi là đúng mười lesson subjects 21–30 theo thứ tự và đủ
`docs/lessons/21-*.md` đến `docs/lessons/30-*.md`. Một operational `fix(ci)` có thể tồn tại giữa các
lesson mà không thay đổi số lesson commits; không rewrite lịch sử đã dùng để học.

## Phase 4 bắt đầu ở đâu

Phase 3 cố ý chưa extract Barista/Kitchen thành process riêng. PostgreSQL vẫn là một instance với
module-owned schemas; API process vẫn host các handlers. Phase 4 có thể bắt đầu process extraction,
deployment topology và distributed capstone mà không phải thiết kế lại contracts, Outbox/Inbox hay
observability boundaries đã khóa tại checkpoint này.

## Summary kiến thức

- Reliable messaging bắt đầu bằng local atomicity và idempotency, không bắt đầu bằng broker feature.
- Outbox xử lý database-to-broker gap; Inbox xử lý broker-to-business duplicate gap.
- Retry, DLT và schema evolution là explicit operational contracts.
- Correlation/causation và trace identity trả lời các câu hỏi khác nhau.
- Metrics cần cardinality budget; trace/log dùng safe identifiers để drill-down.
- Adapter abstraction phải giữ common semantics nhưng vẫn công bố transport differences.
- Fresh-state smoke và commit-level green gates biến curriculum thành executable learning history.
