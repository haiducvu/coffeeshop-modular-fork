# Lesson 29 — OpenTelemetry cho distributed order processing

Lesson 27 đã tạo business correlation/causation xuyên HTTP, Outbox, Kafka và SignalR. Hai identity này giúp
trả lời “những message nào thuộc cùng order workflow?”, nhưng chưa mô tả execution tree, latency hay
reliability trend. Bài này thêm OpenTelemetry trace và metric mà không thay đổi business behavior.

## Mục đích bài học

Sau bài này, một workflow đặt món có thể quan sát qua hai góc nhìn bổ sung nhau:

- distributed trace cho biết request/message đi qua boundary nào và mất bao lâu;
- low-cardinality metrics cho biết publish/consume, Outbox, duplicate Inbox, retry và DLT có xu hướng gì;
- business correlation/causation vẫn giữ ngữ nghĩa domain, không bị thay thế bởi trace ID;
- exporter là opt-in, vì vậy API vẫn chạy khi không có Collector.

## Ba loại identity không thể thay thế nhau

| Identity | Trả lời câu hỏi | Vòng đời |
| --- | --- | --- |
| Correlation ID | Những message nào thuộc cùng business workflow? | Ổn định qua retry và nhiều transaction |
| Causation ID | Message/business action nào trực tiếp gây ra message này? | Mỗi event hop |
| Trace/span ID | Execution nào là parent, child và mất bao lâu? | Mỗi delivery/execution attempt |

Retry giữ business identity nhưng có thể tạo consumer span mới. Vì vậy không dùng trace ID làm order ID,
không dùng correlation ID làm span ID, và không gán retry delivery thành cùng một execution.

## Trace parenting qua transaction và broker

Outbox ngắt execution hiện tại khỏi broker I/O. Trace context vì thế phải được snapshot cùng Outbox
record, sau đó dùng làm remote parent khi worker publish:

```text
HTTP server Activity
  └─ persisted traceparent/tracestate trong Counter Outbox
      └─ Kafka producer Activity
          └─ traceparent/tracestate trong Kafka headers
              └─ Barista/Kitchen/Counter consumer Activity
                  └─ persisted context trong module Outbox tiếp theo
                      └─ Kafka producer Activity tiếp theo
```

Producer không copy nguyên parent cũ vào Kafka header. Nó tạo producer span trước, rồi inject ID của span
này. Consumer extract header làm remote parent, tạo consumer span và snapshot span đó cho Outbox mới.
Nhờ vậy trace là một cây parent/child thật thay vì nhiều span anh em cù trỏ về HTTP root.

`ActivitySource` được đặt trong `CoffeeShop.Messaging.Abstractions`. Kafka adapter tạo `Producer` và
`Consumer` activities, còn module chỉ tiếp tục lưu broker-neutral `MessageIdentity`. HTTP, outgoing HTTP và
EF Core spans do OpenTelemetry instrumentation tự động tạo.

## Metrics và cardinality budget

`MessagingTelemetry` sở hữu một `Meter` với các instruments:

- publish/consume count và processing duration;
- Outbox pending batch, publish attempts và failures;
- Inbox duplicates;
- retry forwarded và dead-letter forwarded.

Metric tags chỉ được phép dùng `event.type`, `module`, destination/topic, `operation`, `result` và
`retry.level`. Order ID, message ID, correlation ID, loyalty identity, payload và exception text bị loại khỏi
dimensions vì chúng tạo số series gần bằng số request. Business IDs vẫn có thể nằm trong span/log để
drill-down một workflow cụ thể.

Unit tests dùng `ActivityListener` để kiểm parent chain và `MeterListener` để khóa instrument/tag
contract. Integration test trên Kafka thật chứng minh trace tiếp tục qua Outbox, producer, ba consumer
roles, notification event và fulfillment.

## Đăng ký OpenTelemetry và exporter opt-in

API dùng một pipeline `AddOpenTelemetry().WithTracing(...).WithMetrics(...)` và resource name
`coffeeshop-api`. Bản trong bài:

- OpenTelemetry hosting, OTLP exporter, ASP.NET Core, HTTP và runtime instrumentation `1.18.0`;
- EF Core instrumentation `1.18.0-beta.1`, được pin prerelease rõ ràng vì chưa có bản stable
  tương ứng tại thời điểm bài học.

Không cấu hình `OpenTelemetry:OtlpEndpoint` thì SDK/provider vẫn được đăng ký nhưng không có
network exporter. Khi có giá trị, startup chỉ chấp nhận canonical HTTP/HTTPS origin, không user info,
path, query hay fragment. Cấu hình sai fail ngay lúc startup thay vì âm thầm mất telemetry.

Exporter/Collector không tham gia `/health/ready`. Telemetry là best-effort diagnostic path; Collector mất
không được làm API ngừng nhận business traffic. Smoke observability kiểm telemetry riêng, nên CI vẫn
phát hiện exporter/configuration hỏng.

## Collector và Jaeger opt-in

Profile `observability` thêm OpenTelemetry Collector Contrib và Jaeger v2. API gửi OTLP/gRPC tới
Collector; Collector expose Prometheus-format metrics và forward traces sang Jaeger. Bài này cố ý không
thêm Prometheus server hoặc Grafana để giữ scope tập trung vào instrumentation/export path.

Chạy fresh observability workflow:

```bash
docker compose down --volumes --remove-orphans

OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317 \
OTEL_METRIC_EXPORT_INTERVAL=5000 \
docker compose --profile observability up -d --build \
  postgres redis kafka schema-registry jaeger otel-collector api

OTEL_METRICS_URL=http://localhost:9464/metrics \
JAEGER_URL=http://localhost:16686 \
./scripts/phase-3-smoke.sh

docker compose --profile observability down --volumes --remove-orphans
```

Các endpoint local:

- Jaeger UI: <http://localhost:16686>;
- Collector Prometheus metrics: <http://localhost:9464/metrics>;
- Collector health extension: <http://localhost:13133>.

Smoke chạy order workflow và poison message/DLT, sau đó đợi bounded cho publish, consume, Outbox và
dead-letter metrics. Nó cũng truy vấn Jaeger service/trace APIs để chứng minh producer/consumer spans
đã được export, không chỉ chứng minh container đang chạy.

## Verification

```bash
dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj -c Release \
  --filter 'FullyQualifiedName~Telemetry|FullyQualifiedName~KafkaRetryRouterTests'

dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj -c Release \
  --filter FullyQualifiedName~OpenTelemetryConfigurationTests

dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj \
  -c Release --filter FullyQualifiedName~CorrelationContinuityTests

dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj \
  -c Release --filter FullyQualifiedName~InboxIdempotencyTests

./tests/scripts/phase-3-smoke-tests.sh
```

Sau các focused proofs, chạy full solution gates và fresh Compose workflow như phần trên.

## Summary kiến thức

- Correlation/causation là business identity; trace/span ID là execution identity.
- Outbox phải persist W3C context tại transaction boundary để worker bất đầu producer span đúng parent.
- Kafka header phải mang producer span context, không copy nguyên HTTP/consumer parent cũ.
- Consumer extract remote parent trước khi tạo span và snapshot span mới cho outgoing Outbox.
- `ActivitySource`/`Meter` broker-neutral giữ module không phụ thuộc OpenTelemetry SDK hay Kafka type.
- Auto-instrumentation phù hợp với framework boundaries; custom instrumentation phù hợp với business/messaging boundaries.
- Metric dimensions cần cardinality budget; business ID, payload và exception text không được làm tag.
- Span/log có thể giữ safe identifiers để drill-down vì chúng không tạo metric time series.
- Retry là execution attempt mới nhưng vẫn thuộc cùng business workflow.
- Conditional exporter giữ local/test mode không phụ thuộc Collector; endpoint sai phải fail-fast.
- Collector tách application instrumentation khỏi telemetry backend và fan-out traces/metrics theo pipeline.
- Observability backend không phải business readiness dependency, nhưng cần smoke gate riêng.
- Telemetry smoke phải assert exported signal có business meaning, không chỉ assert process healthy.
