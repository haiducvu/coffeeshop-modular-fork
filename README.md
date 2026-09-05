# CoffeeShop Modular — .NET 10 Learning Curriculum

Khóa học thực hành xây dựng lại [coffeeshop-modular](https://github.com/thangchung/coffeeshop-modular) bằng .NET 10, sau đó cải tiến thành modular monolith và hệ thống event-driven sử dụng Kafka.

## Cách học

Mỗi commit `lesson(NN)` là một bài học có thể build và test độc lập. Checkout commit, đọc tài liệu tương ứng trong `docs/lessons`, chạy:

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --no-restore
dotnet test CoffeeShop.slnx --no-build
```

## Chạy toàn bộ Phase 1

```bash
docker compose up -d --build postgres redis kafka schema-registry api barista-worker kitchen-worker signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
```

Client chạy tại <http://localhost:5173>. DataGen là profile opt-in:

```bash
docker compose --profile demo run --rm datagen
```

JWT Bearer authentication cũng là opt-in. Profile `identity` chạy Keycloak local,
mount realm import read-only và chỉ publish Keycloak trên loopback. Identity smoke cần
Docker Compose, `curl` và `jq` trên host:

```bash
AUTHENTICATION_ENABLED=true docker compose --profile identity up -d --build \
  postgres redis kafka schema-registry keycloak api barista-worker kitchen-worker
./scripts/phase-2-identity-smoke.sh
```

Từ Lesson 18, `/v2` chỉ được map khi authentication bật: customer tạo/đọc đơn mình,
fulfillment-reader (hoặc operator) đọc queue, operator dùng operational routes và override
ownership có kiểm soát. `/v1`, `/message`, health và DataGen tiếp tục public. Khi auth tắt,
`/v2` trả `404` fail-closed, không tạo identity giả hay bypass policy.

`lesson17-user` / `lesson17-local`, các identity Lesson 18 và bootstrap admin credentials là dữ liệu local
không bí mật để học và smoke test; không tái sử dụng trong production. Khi auth tắt,
host không tạo identity giả và toàn bộ `/v1`, `/message`, DataGen vẫn public như Phase 1.

Lesson 19 bổ sung Redis cho read model fulfillment theo cache-aside. Cache chỉ bật khi host cung cấp
`ConnectionStrings__Redis`; không có cấu hình (bao gồm môi trường `Testing`) thì Counter tiếp tục đọc
PostgreSQL trực tiếp. Compose khởi động `redis:8-alpine` trên loopback, API chờ Redis healthy và smoke
kiểm tra key `fulfilled-orders:v1` sau khi fulfillment hoàn tất. TTL mặc định là một phút và được giới
hạn từ 5 giây đến 1 giờ; có thể đặt bằng `FulfillmentCache__TimeToLive`. Cache miss và invalidation dùng
một gate chung trong một API process để stale reader không ghi đè `DEL`; nhiều API replica cần distributed
fencing/CAS riêng, ngoài phạm vi Lesson 19.

Lesson 20 hoàn tất [checkpoint Phase 2](docs/checkpoints/phase-2.md) bằng newline-delimited JSON logs và
health contract tách bạch: `/health/live` chỉ kiểm process; `/health/ready` kiểm PostgreSQL cùng Redis và
OIDC discovery khi được bật. Readiness response chỉ công bố tên/status/duration. Redis probe tái sử dụng
đúng shared multiplexer của cache, identity probe dùng named `HttpClient` có timeout ngắn. Xem
[tài liệu Lesson 20](docs/lessons/20-operational-foundations.md) để hiểu startup validation và redaction.

Lesson 21 mở Phase 3 bằng assembly `CoffeeShop.IntegrationContracts` broker-neutral. Hai Version 1 event
dùng semantic wire name, payload tối thiểu không chứa loyalty identity và golden JSON fixture để phát hiện
breaking change trước khi Kafka xuất hiện. Xem [tài liệu Lesson 21](docs/lessons/21-versioned-integration-events.md).

Lesson 22 thêm messaging ports broker-neutral và adapter Kafka JSON dùng `acks=all`, idempotent producer,
manual offset commit và hosted consumer shutdown sạch. Tại commit Lesson 22–24, Kafka 4.1.1 chạy opt-in qua
profile `messaging`; từ Lesson 25 broker trở thành dependency mặc định. Testcontainers kiểm tra
round-trip cùng offset commit trên broker thật. Xem [tài liệu Lesson 22](docs/lessons/22-kafka-json-transport.md).

Lesson 23 thêm Transactional Outbox do Counter sở hữu. Mỗi order và một canonical `OrderPlacedV1` Outbox
row được track bởi cùng `CounterDbContext` rồi commit bằng một `SaveChangesAsync`; PostgreSQL test chứng minh
Outbox lỗi sẽ rollback cả order. Payload không chứa loyalty identity và chưa được publish cho tới Lesson 24,
nên fulfillment in-process vẫn giữ nguyên. Xem [tài liệu Lesson 23](docs/lessons/23-transactional-outbox.md).

Lesson 24 drain pending Counter Outbox rows sang Kafka bằng bounded batch và lease cạnh tranh an toàn với
`FOR UPDATE SKIP LOCKED`. Claim transaction kết thúc trước broker I/O; success/failure chỉ được ghi khi đúng
lease, còn lease hết hạn cho phép reclaim sau crash. Kafka vẫn là shadow path nên HTTP fulfillment không đổi.
Real-broker test chứng minh crash sau ACK có thể publish lại cùng message ID — semantics at-least-once mà
Inbox ở Lesson 25 phải xử lý. Xem [tài liệu Lesson 24](docs/lessons/24-outbox-publisher.md).

Lesson 25 chuyển fulfillment thật sang Kafka trong một atomic composition cutover. Barista, Kitchen và Counter
có module-local Inbox; Inbox row, business effect và outgoing Outbox cùng commit bằng một `SaveChangesAsync`.
Duplicate delivery trở thành no-op, còn offset chỉ commit sau database success. Kafka nay chạy mặc định trong
Compose; HTTP, SignalR và Redis behavior được giữ nguyên nhưng fulfillment là eventual. Chạy fresh workflow:

```bash
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka schema-registry api barista-worker kitchen-worker signalr-client
./scripts/phase-4-kitchen-smoke.sh
```

Xem [tài liệu Lesson 25](docs/lessons/25-idempotent-inbox.md).

Lesson 26 giới hạn consumer-processing retry qua hai delay topic rồi giữ poison message trong DLT. Transient
failure đi qua `retry.1` và `retry.2`; contract/validation failure đi thẳng DLT. Original key, bytes và envelope
identity được giữ nguyên, metadata chỉ dùng safe error code. Offset nguồn chỉ commit sau business success,
Inbox duplicate hoặc retry/DLT publish đã được Kafka ACK. Xem
[tài liệu Lesson 26](docs/lessons/26-retry-and-dead-letter.md) và
[DLT replay runbook](docs/operations/kafka-dead-letter-replay.md).

Lesson 27 tạo server-owned correlation tại HTTP boundary rồi snapshot identity vào từng module Outbox.
Business event mới giữ workflow correlation và dùng inbound `MessageId` làm direct causation; retry vẫn giữ
nguyên identity. Kafka headers, consumer scope, structured logs và SignalR notifications cùng mang chuỗi này,
còn trace context được giữ riêng để Lesson 29 instrument. Xem
[tài liệu Lesson 27](docs/lessons/27-correlation-and-causation.md).

Lesson 28 thêm schema-first Avro contracts và Confluent Schema Registry mà vẫn giữ canonical Outbox JSON.
Consumer đọc song song JSON/Avro trong reader-first rollout; Compose mặc định phát Avro, dùng `BACKWARD`
compatibility và Record Name Strategy để original/retry/DLT cùng một subject. Real registry tests khóa additive
field default và breaking fixture. Xem
[tài liệu Lesson 28](docs/lessons/28-avro-schema-evolution.md).

Lesson 29 nối W3C trace qua HTTP, persisted Outbox context, Kafka producer/consumer và Outbox tiếp theo;
business correlation vẫn là identity riêng. `ActivitySource` và `Meter` broker-neutral phát span cùng metric
low-cardinality cho publish/consume, Outbox, Inbox, retry và DLT. OTLP exporter chỉ bật khi endpoint hợp lệ
được cấu hình; profile `observability` opt-in thêm OpenTelemetry Collector và Jaeger mà không làm
chúng thành business-readiness dependency. Xem
[tài liệu Lesson 29](docs/lessons/29-opentelemetry.md).

Lesson 30 hoàn tất [checkpoint Phase 3](docs/checkpoints/phase-3.md) bằng một Dapr pub/sub adapter opt-in.
Kafka vẫn là mặc định và reference reliability path; Dapr dùng cùng semantic topics, envelope, Outbox/Inbox,
handler và telemetry ports qua sidecar có Kafka component. Architecture tests giữ Dapr ngoài contracts/module;
readiness kiểm sidecar HTTP, publisher cấu hình riêng gRPC data plane và app-channel token bảo vệ callback.
Chạy fresh Dapr workflow (override `DAPR_APP_API_TOKEN` bằng secret thật khi deploy):

```bash
docker compose --profile dapr down --volumes --remove-orphans
docker compose -f compose.yaml -f compose.dapr.yaml --profile dapr up -d --build \
  postgres redis kafka api dapr-sidecar
BARISTA_HOSTING_MODE=Embedded KITCHEN_HOSTING_MODE=Embedded \
  MESSAGING_ADAPTER=Dapr ./scripts/phase-3-smoke.sh
```

Xem [tài liệu Lesson 30](docs/lessons/30-dapr-pubsub-adapter.md) để so sánh app-owned Kafka retry/DLT với
runtime-owned Dapr delivery semantics và hiểu trade-off của sidecar.

Lesson 31 mở Phase 4 bằng vertical slice đầu tiên được tách khỏi API: Barista chạy trong một .NET 10
Generic Host riêng, tự migrate schema, consume `OrderPlacedV1`, ghi Inbox/business effect/Outbox và publish
`OrderItemPreparedV1`. API chọn ownership tường minh qua `Modules:Barista:Hosting`; Compose Kafka mặc định
`External`, còn Dapr regression phải đặt `Embedded` vì chưa có sidecar riêng cho Worker. Cả hai process vẫn
dùng cùng PostgreSQL vật lý trong bài này; Lesson 33 mới cô lập database/credential. Chạy fresh proof:

```bash
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka schema-registry api barista-worker kitchen-worker signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
./scripts/phase-4-kitchen-smoke.sh
```

Xem [tài liệu Lesson 31](docs/lessons/31-extract-barista-worker.md).

Lesson 32 lặp lại extraction pattern cho Kitchen mà không tạo “station worker framework” sớm. Kafka Compose
giờ có ba process: API/Counter, Barista Worker và Kitchen Worker. Mỗi Worker tự composition module, consumer
role, Inbox/Outbox, migration, logging và telemetry; API bỏ toàn bộ Kitchen runtime khi
`Modules:Kitchen:Hosting=External`. Mixed order vẫn hoàn tất qua integration contracts Version 1 và Inbox
idempotency. Dapr vẫn là topology embedded rõ ràng cho cả hai station. Chạy proof ba process bằng:

```bash
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka schema-registry \
  api barista-worker kitchen-worker signalr-client
./scripts/phase-4-kitchen-smoke.sh
```

Xem [tài liệu Lesson 32](docs/lessons/32-extract-kitchen-worker.md). Tại checkpoint Lesson 32, database vật lý
vẫn dùng chung có chủ ý.

Lesson 33 thực thi database-per-service: API, Barista và Kitchen lần lượt dùng `coffeeshop_counter`,
`coffeeshop_barista`, `coffeeshop_kitchen` với role riêng, không có quyền CONNECT chéo. Ba logical databases
vẫn nằm trên một PostgreSQL server local. Bootstrap chỉ chạy tự động khi volume mới; volume Lesson 32 không
tự chuyển dữ liệu sang layout mới. Xem [Lesson 33](docs/lessons/33-service-data-ownership.md) và
[data ownership](docs/architecture/service-data-ownership.md) để chạy fresh demo hoặc giữ dữ liệu cũ.
Kafka smoke đọc từng owner bằng credential của service và nối bằng chứng correlation ở phía test.
Dapr embedded dùng override `compose.dapr.yaml`, có database dùng chung riêng cho đường tương thích.

Lesson 34 kiểm chứng batch hữu hạn trên topology này, phát lại event gốc để kiểm Inbox idempotency,
và dừng từng worker sau khi Counter commit rồi quan sát Kafka backlog/phục hồi. Chỉ chạy trên stack
demo riêng, không có DataGen hay người dùng khác gửi order đồng thời:

```bash
./scripts/phase-4-smoke.sh
./scripts/phase-4-fault-demo.sh barista-worker
./scripts/phase-4-fault-demo.sh kitchen-worker
```

Xem [Lesson 34](docs/lessons/34-distributed-flow.md) và
[failure-demo runbook](docs/runbooks/distributed-failure-demo.md) để biết prerequisites, timeout,
recovery, retry/DLT proof và giới hạn của demo.

Dọn containers và database volume local:

```bash
docker compose down --volumes
```

## Bắt đầu Phase 2

Lesson 13 tách Counter, Barista và Kitchen thành các deep module có schema/migration riêng. Đây là phase-boundary reset của learning environment, vì vậy hãy xóa volume Phase 1 trước lần chạy đầu tiên:

```bash
docker compose down --volumes
docker compose up -d --build postgres redis api signalr-client
./scripts/phase-1-smoke.sh
```

Các route `/v1`, SignalR client và DataGen vẫn giữ behavior cũ trên database mới. Volume reset không đại diện cho data-migration strategy của production.

## Lộ trình

- Phase 1 — dựng lại behavior gốc: Lessons 01–12.
- Phase 2 — modular monolith: Lessons 13–20.
- Phase 3 — Kafka và reliable messaging: Lessons 21–30.
- Phase 4 — distributed capstone: Lessons 31–36.

### Bài hiện tại

- [Lesson 01 — Khởi tạo solution .NET 10](docs/lessons/01-bootstrap-dotnet-10.md)
- [Lesson 02 — Endpoint đặt món đầu tiên](docs/lessons/02-place-order-endpoint.md)
- [Lesson 03 — Domain model và menu pricing](docs/lessons/03-order-domain-model.md)
- [Lesson 04 — EF Core và PostgreSQL](docs/lessons/04-ef-core-postgresql.md)
- [Lesson 05 — Query fulfilled orders bằng Specification](docs/lessons/05-query-specifications.md)
- [Lesson 06 — Dispatch use case và validation pipeline](docs/lessons/06-mediatr-validation.md)
- [Lesson 07 — Domain event trong process](docs/lessons/07-domain-events.md)
- [Lesson 08 — Barista async workflow và deterministic time](docs/lessons/08-barista-preparation.md)
- [Lesson 09 — Kitchen workflow và Order completion](docs/lessons/09-kitchen-order-completion.md)
- [Lesson 10 — Typed SignalR updates và TypeScript client](docs/lessons/10-signalr-client.md)
- [Lesson 11 — Data generator hữu hạn và deterministic](docs/lessons/11-data-generator.md)
- [Lesson 12 — Docker Compose và Phase 1 smoke test](docs/lessons/12-docker-compose.md)
- [Lesson 13 — Tách business modules và schema ownership](docs/lessons/13-module-assemblies.md)
- [Lesson 14 — Architecture tests cho module boundary](docs/lessons/14-architecture-tests.md)
- [Lesson 15 — Resource-oriented order API](docs/lessons/15-resource-oriented-api.md)
- [Lesson 16 — Chuẩn hóa API failures bằng Problem Details](docs/lessons/16-problem-details.md)
- [Lesson 17 — Xác thực API client bằng JWT Bearer](docs/lessons/17-jwt-authentication.md)
- [Lesson 18 — Phân quyền thao tác bằng policy](docs/lessons/18-policy-authorization.md)
- [Lesson 19 — Cache fulfillment read model với Redis](docs/lessons/19-redis-read-model-cache.md)
- [Lesson 20 — Structured logs và operational health](docs/lessons/20-operational-foundations.md)
- [Lesson 21 — Versioned integration events](docs/lessons/21-versioned-integration-events.md)
- [Lesson 22 — Kafka JSON transport](docs/lessons/22-kafka-json-transport.md)
- [Lesson 23 — Transactional Outbox](docs/lessons/23-transactional-outbox.md)
- [Lesson 24 — Publish leased Outbox batches](docs/lessons/24-outbox-publisher.md)
- [Lesson 25 — Idempotent Inbox và Kafka fulfillment](docs/lessons/25-idempotent-inbox.md)
- [Lesson 26 — Bounded retry và Dead-Letter Topic](docs/lessons/26-retry-and-dead-letter.md)
- [Lesson 27 — Correlation và causation xuyên HTTP/Kafka](docs/lessons/27-correlation-and-causation.md)
- [Lesson 28 — Avro và Schema Registry governance](docs/lessons/28-avro-schema-evolution.md)
- [Lesson 29 — OpenTelemetry cho distributed workflow](docs/lessons/29-opentelemetry.md)
- [Lesson 30 — Dapr pub/sub adapter opt-in](docs/lessons/30-dapr-pubsub-adapter.md)
- [Lesson 31 — Tách Barista thành Worker độc lập](docs/lessons/31-extract-barista-worker.md)
- [Lesson 32 — Tách Kitchen thành Worker độc lập](docs/lessons/32-extract-kitchen-worker.md)
- [Lesson 33 — Thực thi data ownership của từng service](docs/lessons/33-service-data-ownership.md)
- [Lesson 34 — Kiểm chứng distributed flow và phục hồi worker](docs/lessons/34-distributed-flow.md)

## Nhánh Git

- `original/dotnet7`: đầy đủ 15 commit của source gốc để đối chiếu.
- `learning/dotnet10-rebuild`: lịch sử khóa học tuyến tính.
- `planning/dotnet10-curriculum`: design spec và implementation plans.

## Attribution

Behavior và ý tưởng ban đầu dựa trên dự án của Thang Chung. Bản fork giữ nguyên giấy phép MIT; những thay đổi .NET 10 và tài liệu tiếng Việt phục vụ mục đích học tập.
