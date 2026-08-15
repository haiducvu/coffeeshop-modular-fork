# Checkpoint Phase 2 — Modular monolith

Phase 2 kết thúc sau tám commit Lessons 13–20. Checkpoint chứng minh mỗi commit học được độc lập và HEAD
giữ toàn bộ behavior Phase 1.

## Năng lực đã tích lũy

- Lesson 13–14: Counter, Barista, Kitchen có assembly/schema riêng; architecture tests khóa dependency và
  domain purity.
- Lesson 15–16: `/v2` resource API và Problem Details chuẩn hóa failure contract.
- Lesson 17–18: JWT Bearer opt-in, policy/role/ownership fail-closed; `/v1`, `/message`, health và DataGen
  vẫn public.
- Lesson 19: fulfillment read model cache-aside Redis, fallback PostgreSQL, invalidation và metrics không
  gắn sensitive label.
- Lesson 20: JSON structured logs, startup validation, process liveness và dependency-aware readiness.

## Verification contract tại HEAD

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --configuration Release --no-restore
dotnet test CoffeeShop.slnx --configuration Release --no-build
docker run --rm -v "$PWD:/work" -w /work/src/CoffeeShop.SignalRClient node:22-alpine \
  sh -c 'npm ci && npm run build'
docker compose --profile demo --profile identity build
docker compose up -d postgres redis api signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
docker compose --profile demo run --rm -e OrderGenerator__OrderCount=1 datagen
docker compose --profile identity down --volumes --remove-orphans
AUTHENTICATION_ENABLED=true docker compose --profile identity up -d postgres redis keycloak api
./scripts/phase-2-identity-smoke.sh
docker compose --profile identity down --volumes --remove-orphans
```

Audit lịch sử tạo worktree tạm cho từng commit `lesson(13)` đến `lesson(20)`, chạy restore/build/test và
verification đặc thù đã xuất hiện tại lesson đó. Không push/tag checkpoint nếu một commit không tự build,
test hoặc giữ behavior đã học trước đó.

## Operational contract cuối phase

```text
/health/live  -> process only
/health/ready -> PostgreSQL + enabled Redis + enabled identity discovery
```

Response health không chứa description/exception/data. JSON logs không chứa Authorization, token,
password, connection-string credential hay complete order payload. Redis readiness dùng cùng multiplexer
với cache; identity readiness dùng named client và bounded timeout.

Phase 3 mới đưa Kafka và reliable messaging vào hệ thống. Không có Kafka, OpenTelemetry hay Dapr trong
checkpoint này.
