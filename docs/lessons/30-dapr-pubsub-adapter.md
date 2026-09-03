# Lesson 30 — Thay adapter messaging bằng Dapr pub/sub

Lessons 21–29 đã xây một reliability path có chủ đích trên Kafka: contract versioned, Outbox, Inbox,
bounded retry/DLT, schema governance và telemetry. Bài cuối Phase 3 không thay path đó; nó dùng cùng
application ports để chứng minh transport có thể được thay ở composition root bằng Dapr pub/sub.

## Mục đích bài học

Sau bài này, ứng dụng có hai adapter runtime nhưng chỉ một behavior domain:

- Kafka vẫn là adapter mặc định và reference implementation cho retry/DLT;
- Dapr là lựa chọn opt-in qua `Messaging:Adapter=Dapr` và Compose profile `dapr`;
- module chỉ biết `IIntegrationEventPublisher` và `IIntegrationEventHandler<T>`;
- cùng topic semantic, envelope, key, correlation/causation và W3C trace được giữ qua cả hai path;
- sidecar failure làm readiness unhealthy nhưng không làm sai process liveness;
- subscription discovery/callback chỉ chấp nhận app-channel token mà sidecar biết;
- Dapr state, actors, workflows, scheduler và placement không đi vào scope.

## Seam thay transport

`CoffeeShop.Messaging.Abstractions` sở hữu port và semantic topic resolver. Hai adapter nằm ngoài module:

```text
Counter / Barista / Kitchen
  │
  ├── IIntegrationEventPublisher
  └── IIntegrationEventHandler<T>
          ▲
          │ composition root chọn đúng một path
          │
     ┌────┴─────┐
     │          │
   Kafka      Dapr client → sidecar → pubsub.kafka → Kafka
```

Topic mapping chỉ có một nguồn sự thật:

```text
OrderPlacedV1       -> coffeeshop.orders.v1
OrderItemPreparedV1 -> coffeeshop.preparation.v1
```

Kafka adapter không được tham chiếu Dapr và Dapr adapter không được tham chiếu Kafka adapter. Dapr chỉ
biết rằng component tên `coffeeshop-pubsub` cung cấp pub/sub; việc component đó dùng Kafka là deployment
detail trong `deploy/dapr/components/pubsub.yaml`.

## Publisher và hai cổng sidecar

`DaprIntegrationEventPublisher` gọi `DaprClient.PublishEventAsync`, giữ partition key và CloudEvent
metadata gồm message ID, event type, correlation, causation và trace context. Canonical envelope vẫn là
payload; module Outbox không lưu Dapr type.

Dapr .NET client cần phân biệt hai endpoint:

| Endpoint | Mặc định local | Vai trò trong bài |
| --- | --- | --- |
| HTTP | `http://127.0.0.1:3500` | health/metadata API của sidecar |
| gRPC | `http://127.0.0.1:50001` | `PublishEventAsync` của `DaprClient` |

Hai endpoint được validate như canonical HTTP/HTTPS origin và được cấu hình riêng bằng
`DaprClientBuilder.UseHttpEndpoint`/`UseGrpcEndpoint`. Fresh smoke đã bắt được lỗi thực tế khi chỉ cấu hình
3500: readiness vẫn xanh nhưng publish gRPC cố dùng endpoint mặc định sai. Vì vậy readiness và data-plane
proof đều cần thiết; health probe không thay thế một publish/consume smoke.

## Programmatic subscriptions và fan-out

API chỉ expose hai subscription Version 1 khi Dapr được chọn:

```text
coffeeshop.orders.v1      -> POST /dapr/orders/v1
coffeeshop.preparation.v1 -> POST /dapr/preparation/v1
```

Order subscription dispatch tuần tự tới role `barista`, rồi `kitchen`, nhưng luôn thử cả hai role trước khi
ACK. Lỗi transient có độ ưu tiên cao nhất và trả `RETRY`; nếu không có transient nhưng có permanent failure
thì trả `DROP`. Vì vậy lỗi permanent riêng của Barista không được phép làm mất delivery dành cho Kitchen.
Nếu Kitchen transient-fail sau khi Barista commit, Dapr redeliver toàn message; Inbox của Barista biến
delivery lặp thành no-op và Kitchen có cơ hội chạy lại. Preparation subscription dispatch tới `counter`.
Cách này giữ handler seam của Kafka và không đưa Dapr attribute/API vào module.

Endpoint trả delivery status theo Dapr contract:

- `SUCCESS`: mọi handler đã commit hoặc Inbox xác nhận duplicate;
- `RETRY`: lỗi transient, yêu cầu Dapr redeliver;
- `DROP`: contract/validation failure permanent;
- host cancellation không bị đổi thành success/drop mà tiếp tục propagate.

JSON/CloudEvent binding cũng nằm trong adapter boundary. Payload malformed được trả HTTP 200 kèm `DROP`,
thay vì để ASP.NET trả 400 khiến Dapr retry vô hạn một message không thể sửa.

## Dapr không xóa khác biệt delivery semantics

| Khía cạnh | Kafka adapter mặc định | Dapr adapter opt-in |
| --- | --- | --- |
| Broker API | Confluent client trực tiếp | Dapr API, component dùng Kafka |
| Consumer retry | Hai retry topics với delay bounded | Runtime/component quản lý redelivery |
| Poison record | Application DLT + replay runbook | `DROP`; bài này không cấu hình Dapr dead-letter topic |
| Offset/ACK | Application commit offset có chủ đích | Dapr sidecar/component sở hữu ACK |
| Schema Registry | Avro writer/reader được application kiểm soát | Dapr path publish JSON envelope |
| Duplicate protection | Module Inbox | Vẫn là module Inbox |
| Telemetry | custom Kafka producer/consumer activities | custom Dapr producer/consumer activities |

Do đó Dapr là một abstraction trade-off, không phải “Kafka nhưng ít code hơn” theo nghĩa tuyệt đối.
Sidecar giảm broker SDK/configuration trong application, nhưng chuyển retry, ACK, component lifecycle và
một phần observability sang runtime. Khi cần app-owned bounded retry, DLT schema và replay control của
Phase 3, Kafka adapter vẫn là reference path.

## Sidecar profile và readiness

Profile `dapr` chạy `daprio/daprd:1.18.3`, còn .NET SDK packages được pin `1.18.5`. Chúng giao tiếp qua
Dapr stable APIs; patch versions được pin riêng theo artifact hiện có. Sidecar:

- gọi API app channel tại `api:8080`;
- expose HTTP API local tại port 3500;
- load duy nhất Kafka pub/sub component read-only;
- đặt `initialOffset=oldest` để consumer group mới không bỏ event được publish trong startup race;
- không chạy placement vì không có actor/workflow;
- phụ thuộc Kafka healthy, trong khi API readiness phụ thuộc `/v1.0/healthz` của sidecar.

`APP_API_TOKEN` được cấu hình cho sidecar và cùng secret được bind vào
`Messaging:Dapr:AppApiToken`. Middleware so sánh header `dapr-api-token` theo constant time trước khi
CloudEvent binding chạy, cho cả `/dapr/subscribe` và hai callback. Compose có token local để bài học chạy
ngay; deployment thật phải override `DAPR_APP_API_TOKEN` bằng secret không nằm trong source control.

Liveness vẫn chỉ kiểm process. Khi chọn Dapr, readiness có `dapr`, PostgreSQL và Redis; nó không công bố
Kafka/Schema Registry check từ application vì API không kết nối trực tiếp hai dependency đó trên path này.
Kafka health vẫn là dependency vận hành của sidecar trong Compose.

`Messaging:Kafka:Enabled` được giữ làm master messaging switch để không phá behavior/configuration của các
lesson trước. Nó phải là `true` khi chọn cả Kafka hoặc Dapr; `Messaging:Adapter` mới quyết định adapter nào
được đăng ký.

## Chạy adapter Dapr

Luôn dùng fresh volumes cho acceptance proof:

```bash
docker compose --profile dapr down --volumes --remove-orphans

MESSAGING_ADAPTER=Dapr \
docker compose --profile dapr up -d --build \
  postgres redis kafka api dapr-sidecar

MESSAGING_ADAPTER=Dapr ./scripts/phase-3-smoke.sh

docker compose --profile dapr down --volumes --remove-orphans
```

Smoke không chỉ ping process. Nó kiểm:

- readiness có đúng Dapr/data-store dependencies;
- metadata có component `pubsub.kafka` và đúng hai subscriptions;
- mixed order đi hết Outbox → Dapr → Kafka → Inbox → fulfillment;
- Redis cache tồn tại;
- station, Inbox, pending/rejected Outbox counts chính xác;
- correlation, direct causation và trace ID liên tục.

## Test strategy

```bash
dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj -c Release \
  --filter 'FullyQualifiedName~MessagingAdapterContractTests|FullyQualifiedName~DaprSubscriptionDispatcherTests'

dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj -c Release \
  --filter 'FullyQualifiedName~MessagingAdapterConfigurationTests|FullyQualifiedName~Dapr_sidecar_failure'

dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj \
  -c Release --filter FullyQualifiedName~DaprAdapterTests

dotnet test tests/CoffeeShop.ArchitectureTests/CoffeeShop.ArchitectureTests.csproj -c Release
./tests/scripts/phase-3-smoke-tests.sh
```

Contract tests khóa shared topic/identity/cancellation; dispatcher tests khóa success/retry/drop; PostgreSQL
integration test chứng minh duplicate Dapr delivery chỉ tạo một effect/Inbox/Outbox mỗi station; architecture
tests giữ framework ở API/Dapr adapter; real smoke kiểm toàn bộ runtime path.

## Failure scenarios cần nhớ

- Sidecar chết: `/health/live` vẫn 200, `/health/ready` thành 503 và Outbox giữ message để retry.
- gRPC endpoint sai nhưng HTTP endpoint đúng: health có thể xanh, publish vẫn fail; data-plane smoke bắt lỗi.
- Component không load hoặc thiếu subscription: metadata assertion fail trước khi tạo order.
- Consumer group chưa assign partition: `oldest` vẫn đọc event đầu tiên sau khi group sẵn sàng.
- Handler transient-fail: endpoint trả `RETRY`; handler đã commit trước đó được Inbox deduplicate.
- Contract permanent-fail: endpoint trả `DROP`; không có application DLT trên Dapr path của bài này.
- CloudEvent malformed: endpoint ACK `DROP`, không retry vô hạn; request thiếu/sai app token bị 401.
- API dừng giữa delivery: runtime redeliver và Inbox giữ business effect idempotent.
- Chọn adapter không hợp lệ: startup fail-fast, không âm thầm fallback Kafka.

## Summary kiến thức

- Hexagonal seam chỉ có giá trị khi contract, topic và identity thật sự broker-neutral.
- Transport substitution diễn ra ở composition root; module không cần biết adapter được chọn.
- Dapr sidecar tách application khỏi broker client nhưng không loại bỏ delivery semantics hay vận hành.
- Control-plane health/metadata và data-plane publish/consume là hai proof khác nhau.
- Dapr .NET client dùng HTTP và gRPC cho các mục đích khác nhau; phải cấu hình/validate cả hai endpoint.
- Programmatic subscription giữ route discovery explicit và testable.
- Fan-out phải thử mọi logical consumer; permanent failure của một role không được làm đói role còn lại.
- App-channel token giữ subscription routes ngoài public trust boundary dù host API có expose port.
- `SUCCESS`, `RETRY`, `DROP` phải xuất phát từ failure classification có chủ đích.
- Runtime-managed retry khác application-owned retry topic/DLT; abstraction không được che giấu khác biệt đó.
- Outbox giữ accepted work khi sidecar/broker unavailable; Inbox giữ correctness khi redelivery xảy ra.
- Placement không cần cho pub/sub-only workload; thêm infrastructure không dùng chỉ tăng failure surface.
- Architecture fitness functions ngăn Dapr framework lan vào contracts, abstractions và business modules.
- Kafka mặc định và Dapr opt-in cho phép học trade-off mà không làm yếu reliability reference path.
