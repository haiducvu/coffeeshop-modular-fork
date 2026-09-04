# Lesson 32 — Tách Kitchen thành Worker độc lập

Lesson 31 đã chứng minh có thể chuyển một vertical slice từ modular monolith sang process riêng mà không đổi
integration contract. Lesson 32 áp dụng lại pattern đó cho Kitchen. Kết quả là topology Kafka có ba process
độc lập: API/Counter, Barista Worker và Kitchen Worker; mixed order vẫn hoàn tất với behavior cũ.

## Mục đích bài học

Sau bài này:

- `CoffeeShop.Kitchen.Worker` tự composition Kitchen module, Kafka consumer, Inbox/Outbox và telemetry;
- Worker validate cấu hình rồi migrate schema Kitchen trước khi bắt đầu consume;
- API chỉ host Kitchen khi `Modules:Kitchen:Hosting=Embedded`;
- Kafka Compose mặc định đặt cả Barista và Kitchen ở mode `External`;
- Dapr regression đặt cả hai station ở mode `Embedded` và không chạy Worker;
- integration test chứng minh một mixed order đi qua đúng ba host độc lập;
- architecture test ngăn hai Worker phụ thuộc API, module còn lại, Worker còn lại hoặc Dapr;
- không tạo abstraction dùng chung chỉ vì hai composition root hiện trông giống nhau.

## Vertical slice trước và sau extraction

Sau Lesson 31, Kitchen vẫn chạy cùng API:

```text
Process API (Counter + Kitchen)                 Process Barista Worker
HTTP -> Counter Outbox -> Kafka orders --------> Barista Inbox/item/Outbox
               |              |
               |              +---------------> API/Kitchen Inbox/item/Outbox
               +<---------------- Kafka preparation <--------+
```

Sau Lesson 32, mỗi station là một process riêng:

```text
HTTP / SignalR
      |
      v
CoffeeShop.Api (Counter)
      | Counter Outbox -> coffeeshop.orders.v1
      +-------------------------+-------------------------+
                                |                         |
                                v                         v
                   CoffeeShop.Barista.Worker  CoffeeShop.Kitchen.Worker
                   Inbox -> drink -> Outbox    Inbox -> food -> Outbox
                                |                         |
                                +---- coffeeshop.preparation.v1 ----+
                                                                  |
                                                                  v
                                                     Counter Inbox -> Fulfilled
```

API không gọi HTTP đồng bộ sang Worker và Worker không gọi ngược API. Kafka cùng
`CoffeeShop.IntegrationContracts` là integration boundary; database không phải integration API.

## Composition root của Kitchen Worker

Entry point dùng .NET 10 Generic Host:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddKitchenWorkerLogging();
builder.Services.AddKitchenWorker(builder.Configuration);

using var host = builder.Build();
host.Services.ValidateKitchenWorkerOptions();
await host.Services.MigrateKitchenModuleAsync();
await host.RunAsync();
```

`AddKitchenWorker` là public seam duy nhất mà integration tests cần dùng. Nó đăng ký:

- validated `ConnectionStrings:Kitchen` và optional canonical OTLP origin;
- Kafka adapter và đúng một logical role `AddKafkaConsumer<OrderPlacedV1>("kitchen")`;
- Kitchen module, Inbox handler và Kitchen Outbox publisher;
- worker-local `TaskPreparationDelay` và domain-event dispatcher;
- JSON console logging có scopes;
- OpenTelemetry với `service.name=coffeeshop-kitchen-worker`.

Trước migration, Worker resolve Kafka và Kitchen Outbox options để cấu hình sai fail sớm. Migration lỗi làm
process thoát trước khi consumer nhận partition; orchestrator nhìn thấy startup failure thay vì một process
đang chạy trên schema chưa sẵn sàng. `RunAsync` truyền lifecycle cancellation đến hosted consumer và Outbox
worker, nên shutdown không dùng fire-and-forget task.

## Vì sao chưa tạo shared station worker framework

Barista Worker và Kitchen Worker có cấu trúc tương tự: settings, logging, delay, dispatcher, telemetry và
composition method. Tách ngay một framework generic sẽ giảm vài dòng nhưng tạo coupling mới giữa hai bounded
contexts và buộc abstraction phải đoán trước những khác biệt chưa xuất hiện.

Lesson này chọn **bounded duplication**:

- public pattern giống nhau để người học so sánh được;
- implementation, options, migrations và domain types vẫn thuộc từng station;
- architecture tests cấm tham chiếu chéo;
- chỉ trích xuất abstraction khi có nhiều use case ổn định chứng minh seam thật sự chung.

Duplication nhỏ, local và có chủ ý thường rẻ hơn abstraction sai. “Hai đoạn code trông giống nhau” chưa đủ
để kết luận “chúng thay đổi vì cùng một lý do”.

## Explicit hosting mode và single ownership

API resolve riêng hai setting:

```text
Modules:Barista:Hosting = Embedded | External
Modules:Kitchen:Hosting = Embedded | External
```

Với Kitchen:

| Mode | API đăng ký Kitchen module/migration/consumer/Outbox | Kitchen Worker |
| --- | --- | --- |
| `Embedded` | Có | Không chạy |
| `External` | Không | Sở hữu toàn bộ runtime |

Thiếu setting mặc định là `Embedded` để source chạy trực tiếp và các checkpoint cũ giữ behavior. Giá trị như
`Shadow` bị reject. Compose Kafka dùng `External`; chạy API và Worker cùng sở hữu role không phải một mode hợp
lệ vì sẽ làm migration/lifecycle/telemetry ownership mơ hồ.

Dapr của Lesson 30 chỉ có sidecar cho API. Vì chưa có sidecar và subscription endpoint riêng cho Worker,
`Dapr + External` fail-fast cho từng station. Regression Dapr phải đặt cả Barista và Kitchen về `Embedded`.

## At-least-once flow qua ba process

Một mixed order đi theo các local transaction độc lập:

1. Counter commit order và `OrderPlacedV1` vào Counter Outbox.
2. Counter Outbox publish event; hai consumer group `barista` và `kitchen` nhận độc lập.
3. Mỗi Worker commit Inbox row, đúng một station item và `OrderItemPreparedV1` Outbox trong database transaction.
4. Hai station Outbox publish preparation events.
5. Counter consumer ghi hai Inbox rows; order chuyển sang `Fulfilled` khi đủ drink và food.

Integration proof kiểm literal counts `[1,1,1,1,1,1,2]`: mỗi station có một item, một processed Inbox, một
published Outbox; Counter có hai processed Inbox rows. Duplicate delivery vẫn được Inbox idempotency biến
thành no-op. Đây là at-least-once + idempotent effect, không phải distributed transaction hay exactly-once.

## Logical ownership hôm nay, physical isolation ở Lesson 33

Trong Kafka topology, API không đăng ký Kitchen DbContext, migration, consumer hoặc Outbox khi Kitchen là
`External`. Kitchen Worker là code path duy nhất sở hữu schema `kitchen`. Tuy nhiên cả ba process vẫn dùng
cùng PostgreSQL database vật lý trong Lesson 32 để extraction chỉ thay một chiều kiến trúc.

Đây chưa phải database-per-service. Lesson 33 sẽ tách database và runtime credential, rồi kiểm chứng
permission denial giữa services. Không được dùng shared physical database hiện tại như integration contract
hoặc thêm cross-schema query vào business flow.

## Cách chạy test và smoke

Focused gates:

```bash
dotnet test tests/CoffeeShop.WorkerTests/CoffeeShop.WorkerTests.csproj \
  --filter FullyQualifiedName~KitchenWorkerConfigurationTests

dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj \
  --filter 'FullyQualifiedName~KitchenHostingCompositionTests|FullyQualifiedName~external_kitchen'

dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj \
  --filter FullyQualifiedName~ExtractedKitchenWorkflowTests

./tests/scripts/phase-3-smoke-tests.sh
./tests/scripts/phase-4-compose-tests.sh
./tests/scripts/phase-4-kitchen-smoke-tests.sh
```

Fresh Kafka topology:

```bash
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka schema-registry \
  api barista-worker kitchen-worker signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
./scripts/phase-4-kitchen-smoke.sh
```

Fresh Dapr embedded regression:

```bash
docker compose --profile dapr down --volumes --remove-orphans
BARISTA_HOSTING_MODE=Embedded KITCHEN_HOSTING_MODE=Embedded MESSAGING_ADAPTER=Dapr \
  docker compose --profile dapr up -d --build postgres redis kafka api dapr-sidecar
BARISTA_HOSTING_MODE=Embedded KITCHEN_HOSTING_MODE=Embedded MESSAGING_ADAPTER=Dapr \
  ./scripts/phase-3-smoke.sh
docker compose --profile dapr down --volumes --remove-orphans
```

`phase-4-kitchen-smoke.sh` bắt buộc cả hai Worker đang chạy, rồi poll có deadline đến khi migrations tạo đủ
hai station schema và cột cuối `RejectedAtUtc` trước khi delegate Phase 3 workflow proof. Container state
`running` chưa đồng nghĩa Generic Host đã migrate xong. Compose chuyển Kafka retry cùng Outbox overrides đến
đúng Worker sở hữu, đồng thời giữ bản API cho topology embedded. Khi cấu hình observability, smoke còn yêu cầu
Jaeger có cả ba `service.name` và cùng
distributed trace chứa API publish span, Barista process span và Kitchen process span.

## Những lỗi bài học này chủ ý bắt

- Thiếu/invalid Kitchen database hoặc Kafka settings: Worker fail trước migration.
- OTLP endpoint chứa path, query, credential hoặc fragment: reject mà không echo secret.
- Worker đăng ký sai hoặc nhiều logical consumer role: composition test fail.
- API vẫn giữ Kitchen runtime ở mode `External`: host-composition test fail.
- Một Worker không chạy: Phase 4 wrapper fail trước khi gửi order.
- Một Worker không export process-attributed trace: observability smoke fail trong deadline hữu hạn.
- Kitchen Worker phụ thuộc API, Barista, Counter, Barista Worker hoặc Dapr: architecture test fail.
- Dapr ghép với external station chưa có sidecar: startup validation fail.

## Summary kiến thức

- Một extraction pattern tốt phải lặp lại được cho bounded context thứ hai mà không đổi business contract.
- Mỗi process cần composition root, configuration validation, migration lifecycle và telemetry identity riêng.
- Explicit `Embedded|External` giữ single runtime owner và tránh dual consumers phụ thuộc timing.
- Kafka consumer groups cho phép hai station nhận cùng order event nhưng sở hữu effect độc lập.
- Local transaction + Outbox + Inbox là nền tảng chịu at-least-once; không tạo exactly-once toàn hệ thống.
- Bounded duplication bảo vệ module autonomy khi abstraction chung chưa đủ ổn định.
- Architecture fitness functions giữ process boundary khỏi suy thoái theo thời gian.
- Split-host integration test có giá trị hơn unit wiring test vì chứng minh message, persistence và lifecycle nối thật.
- Smoke test phải kiểm process existence lẫn end-to-end effect; health/readiness riêng lẻ chưa chứng minh workflow.
- Distributed tracing phải kiểm process attribution, không chỉ kiểm tên operation xuất hiện.
- Logical schema ownership có thể đi trước physical database isolation nếu giới hạn được ghi rõ.
- Kafka là distributed path; Dapr chỉ xanh ở topology embedded mà bài học thực sự hỗ trợ.

Lesson 32 kết thúc sau commit này. Lesson 33 chưa bắt đầu.
