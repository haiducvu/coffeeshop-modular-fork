# Lesson 31 — Tách Barista thành Worker độc lập

Phase 3 đã làm fulfillment bất đồng bộ qua integration events nhưng Counter, Barista và Kitchen vẫn chạy
chung trong process API. Lesson 31 thực hiện lát cắt đầu tiên của distributed capstone: đưa toàn bộ runtime
ownership của Barista sang một .NET 10 Worker riêng mà không đổi HTTP contract hay business behavior.

## Mục đích bài học

Sau bài này:

- API vẫn nhận order, host Counter và Kitchen, nhưng không host Barista trong topology Kafka mặc định;
- `CoffeeShop.Barista.Worker` tự composition module, Kafka consumer, Outbox publisher và telemetry;
- Worker tự chạy migration Barista trước khi bắt đầu consume;
- `Modules:Barista:Hosting=Embedded|External` xác định duy nhất process nào sở hữu Barista;
- Kafka vẫn giữ at-least-once delivery, còn Inbox giữ business effect idempotent;
- Dapr tiếp tục là regression topology `Embedded`, chưa giả vờ hỗ trợ Worker khi chưa có sidecar riêng;
- API/SignalR/Redis/authorization và integration contract Version 1 không thay đổi.

## Vertical slice trước và sau extraction

Trước Lesson 31, các hosted service nằm trong một process:

```text
HTTP -> API/Counter Outbox -> Kafka
                            ├─ API/Barista -> Barista Inbox + item + Outbox
                            └─ API/Kitchen -> Kitchen Inbox + item + Outbox
preparation topic -> API/Counter Inbox -> Fulfilled
```

Sau extraction trong Kafka Compose:

```text
Process API                                      Process Barista Worker
HTTP -> Counter + Kitchen                        OrderPlacedV1 consumer
  |       |                                      Barista Inbox + item
  |       +-> Kitchen Outbox -> Kafka             Barista Outbox
  +-> Counter Outbox -------> Kafka -----------------^     |
Kafka preparation topic -> Counter Inbox <-------------- Kafka
                              |
                         Fulfilled + SignalR/cache
```

Đây là vertical slice thật: Worker có entry point, lifecycle, cấu hình, migration, consumer, persistence,
Outbox, telemetry, Docker image và integration proof riêng. Module Barista không bị copy hoặc viết lại;
composition root mới tái sử dụng public seam đã xây từ Phase 2–3.

## Vì sao phải chọn `Embedded` hoặc `External`

Nếu API và Worker cùng đăng ký consumer role `barista` với cùng consumer group, Kafka sẽ chia partition giữa
hai process. Hệ thống có thể trông như chạy đúng nhưng ownership deployment trở nên mơ hồ: process nào
migrate, process nào xuất telemetry, process nào cần scale hoặc rollback?

API vì vậy resolve một lựa chọn tường minh:

| Mode | API đăng ký Barista module/migration/consumer/Outbox | Worker |
| --- | --- | --- |
| `Embedded` | Có | Không chạy |
| `External` | Không | Sở hữu toàn bộ Barista runtime |

Thiếu setting vẫn mặc định `Embedded` để checkout các lesson cũ hoặc chạy API trực tiếp không đổi behavior.
Compose Kafka đặt `External` và khởi động `barista-worker`. Giá trị ngoài enum bị reject lúc startup thay vì
âm thầm fallback.

## Startup, migration và cancellation của Worker

Worker dùng Generic Host chuẩn của .NET 10:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBaristaWorker(builder.Configuration);

using var host = builder.Build();
await host.Services.MigrateBaristaModuleAsync();
await host.RunAsync();
```

Thứ tự này là một invariant quan trọng:

1. cấu hình database, Kafka, Outbox và OTLP được validate;
2. dependency graph được build;
3. migration Barista phải thành công;
4. sau đó hosted consumers và Outbox publisher mới chạy;
5. khi host nhận shutdown, cancellation token dừng vòng consume/publish có kiểm soát.

Migration lỗi không bị catch rồi downgrade thành warning. Process thoát để orchestrator thấy startup failure,
tránh consume message trên schema chưa sẵn sàng. Đây là lựa chọn học tập/local deployment; production lớn
thường chạy migration bằng deployment job có kiểm soát thay vì để mọi replica cùng migrate.

`AddBaristaWorker` là composition boundary hoàn chỉnh. Nó đăng ký đúng một consumer role
`AddKafkaConsumer<OrderPlacedV1>("barista")`, Barista module, module Outbox worker, production delay,
domain-event dispatcher và OpenTelemetry. Process dùng JSON console logging với scopes để các trường message,
correlation, causation, event version và consumer role vẫn truy vấn được sau khi tách host; code Worker không
log payload hoặc configuration values. `TryAddSingleton<IPreparationDelay, TaskPreparationDelay>` cho phép
integration test cài no-delay adapter trước mà production behavior vẫn giữ nguyên.

## Outbox/Inbox flow xuyên hai process

Một mixed order đi theo chuỗi:

1. Counter commit Order và `OrderPlacedV1` vào cùng transaction/Outbox.
2. Counter Outbox publish event lên Kafka.
3. Barista Worker consume event bằng group role `barista`.
4. Barista Inbox, item đã pha và `OrderItemPreparedV1` Outbox commit atomically.
5. Barista Outbox publish preparation event.
6. Counter consumer trong API ghi Inbox và cập nhật order.
7. Kitchen chạy nhánh tương tự trong API; khi đủ hai item, order thành `Fulfilled`.

Không có distributed transaction giữa API, Worker, Kafka và PostgreSQL. Correctness đến từ local transaction,
at-least-once delivery và idempotent Inbox. Crash sau broker ACK vẫn có thể tạo duplicate delivery; duplicate
message ID phải thành no-op ở business boundary.

## Schema ownership hôm nay, database isolation ngày mai

Barista Worker là process duy nhất migrate và ghi schema `barista` khi mode là `External`; API bỏ cả module,
migration, consumer và Outbox worker của Barista. Đây là logical ownership.

Lesson 31 chủ ý vẫn dùng cùng PostgreSQL database vật lý với Counter/Kitchen để chỉ thay một chiều tại một
thời điểm. Không được hiểu shared database này là đích kiến trúc cuối. Lesson 33 sẽ tách database và
credential theo service, rồi kiểm permission denial để biến ownership thành boundary hạ tầng thực sự.

## Kafka distributed path và Dapr embedded regression

Kafka là transport của Phase 4 vì Worker đã có consumer process độc lập, retry topics, DLT, Avro/Schema
Registry và offset ownership rõ ràng. Dapr topology của Lesson 30 có một sidecar gắn với API; Lesson 31 chưa
thêm sidecar/subscription host cho Worker. Vì vậy:

- Kafka Compose: `BARISTA_HOSTING_MODE=External`, chạy `barista-worker`;
- Dapr regression: `BARISTA_HOSTING_MODE=Embedded`, không chạy Worker;
- `Dapr + External` fail-fast với thông báo cấu hình rõ ràng.

Giữ giới hạn này tốt hơn một cấu hình “xanh giả” trong đó không process nào nhận Barista event hoặc hai
process cùng tin rằng mình sở hữu nó.

## Cách chạy test và smoke

Focused tests:

```bash
dotnet test tests/CoffeeShop.WorkerTests/CoffeeShop.WorkerTests.csproj \
  --filter FullyQualifiedName~BaristaWorkerConfigurationTests

dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj \
  --filter FullyQualifiedName~ExtractedBaristaWorkflowTests

dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj \
  --filter 'FullyQualifiedName~Barista_hosting_mode|FullyQualifiedName~Dapr_with_external_barista'

./tests/scripts/phase-4-barista-smoke-tests.sh
```

Fresh Kafka topology:

```bash
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka schema-registry api barista-worker signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
./scripts/phase-4-barista-smoke.sh
```

`phase-4-barista-smoke.sh` trước tiên bắt buộc `barista-worker` xuất hiện trong danh sách service đang chạy,
sau đó delegate toàn bộ Phase 3 proof: readiness, mixed order, persistence deltas, correlation/causation,
Redis, schema subjects, retry và DLT. Khi bật observability, wrapper còn yêu cầu Jaeger có service
`coffeeshop-barista-worker` và Barista processing span nằm trong đúng distributed trace của API publisher.

Dapr embedded regression:

```bash
docker compose --profile dapr down --volumes --remove-orphans
BARISTA_HOSTING_MODE=Embedded MESSAGING_ADAPTER=Dapr \
  docker compose --profile dapr up -d --build postgres redis kafka api dapr-sidecar
BARISTA_HOSTING_MODE=Embedded MESSAGING_ADAPTER=Dapr ./scripts/phase-3-smoke.sh
docker compose --profile dapr down --volumes --remove-orphans
```

## Những lỗi bài học này chủ ý bắt

- Worker thiếu connection string hoặc OTLP endpoint không an toàn: fail trước khi host chạy và không echo secret.
- Worker thiếu dependency composition như domain-event dispatcher: split-host test không fulfillment.
- API quên bỏ embedded consumer/module/migration: ownership kép hoặc schema lifecycle kép.
- Worker không chạy trong Compose: Phase 4 wrapper fail trước khi tạo order.
- Worker phụ thuộc API, Counter, Kitchen hoặc Dapr: architecture fitness function fail.
- Dapr được ghép với external Worker chưa hỗ trợ: startup fail thay vì mất message im lặng.

## Summary kiến thức

- Strangler extraction an toàn bắt đầu bằng một vertical slice có thể chạy và kiểm chứng độc lập.
- Composition root sở hữu dependency wiring; business module vẫn broker/host-neutral.
- Deployment mode phải explicit để tránh dual ownership và lỗi phụ thuộc vào timing.
- Generic Host cung cấp lifecycle/cancellation thống nhất cho consumer và background publisher.
- Migrate-before-consume giữ schema readiness trước khi nhận work; production cần chiến lược migration riêng.
- Local transaction + Outbox + Inbox xử lý at-least-once; chúng không tạo exactly-once toàn hệ thống.
- Integration contract ổn định cho phép chuyển process boundary mà không đổi domain behavior.
- Logical schema ownership có thể đi trước physical database isolation, miễn giới hạn được ghi rõ và kiểm thử.
- Telemetry service name riêng giúp phân biệt trace/metric theo process sau extraction.
- Smoke test cần chứng minh cả process existence và business data path; health check đơn lẻ chưa đủ.
- Architecture test biến boundary mong muốn thành fitness function chạy trong CI.
- Kafka là distributed path hiện tại; Dapr chỉ được giữ ở topology mà bài học thực sự hỗ trợ.

Ở checkpoint Lesson 31, Kitchen vẫn nằm trong API; Lesson 32 thực hiện extraction đó trong một commit riêng.
