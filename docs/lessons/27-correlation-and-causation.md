# Lesson 27 — Correlation và causation xuyên HTTP/Kafka

Lesson 26 đã giữ nguyên các header identity khi retry, nhưng workflow vẫn chưa có một nguồn correlation rõ
ràng ở HTTP boundary và trace snapshot chưa được publisher đưa vào Kafka. Bài này tạo một identity chain có
thể đi từ request đặt món đến Outbox, Kafka, consumer, event kế tiếp, structured log và SignalR notification.

## Mục đích bài học

Ba loại identity trả lời ba câu hỏi khác nhau:

| Identity | Câu hỏi | Quy tắc trong CoffeeShop |
|---|---|---|
| `MessageId` | Đây là message nào? | Mỗi business event mới có UUID mới; retry giữ nguyên ID |
| `CorrelationId` | Những operation nào thuộc cùng workflow? | Server sinh một UUID tại HTTP root và mọi event con kế thừa |
| `CausationId` | Message nào trực tiếp tạo operation này? | Root là `null`; event con trỏ tới inbound `MessageId` |

W3C `traceparent`/`tracestate` là distributed-tracing context, không thay thế business identity. Trace có thể
bị sampling hoặc kết thúc theo deployment, trong khi correlation/causation vẫn phải tồn tại trong dữ liệu
Outbox và envelope để điều tra business workflow.

## Server sở hữu root correlation

`CorrelationMiddleware` chạy ở đầu ASP.NET Core pipeline. Với mỗi request hợp lệ, middleware:

1. sinh canonical UUID mới;
2. snapshot W3C trace context hiện tại;
3. push `MessageIdentity` cho request lifetime;
4. mở logging scope với correlation/causation;
5. trả ID qua response header `X-Correlation-ID`.

Client không được chọn business correlation. Nếu client gửi một header UUID hợp lệ, server vẫn tạo ID mới.
Header malformed, duplicated hoặc quá dài bị trả `400` trước khi vào application để tránh log injection và
không biến arbitrary input thành trusted identity.

```text
HTTP POST /orders
  └─ CorrelationId = server UUID
     CausationId   = null
     TraceParent   = ASP.NET Core request Activity
```

## Scoped identity không được leak

`IMessageIdentityAccessor` là broker-neutral boundary. `MessageIdentityAccessor` dùng `AsyncLocal` để identity
đi theo async execution flow, hỗ trợ nested scope và bắt buộc dispose theo thứ tự LIFO. Đọc `Current` ngoài
một request/consumer scope sẽ fail fast thay vì âm thầm sinh identity sai.

Ambient context chỉ hợp lệ trong lúc xử lý hiện tại. Outbox worker chạy sau đó trên background thread, nên
writer phải snapshot bốn field bất biến vào row ngay trong business transaction:

```text
MessageIdentity.Current
  └─ module Outbox writer
       ├─ envelope: correlation + causation
       └─ row:      correlation + causation + traceparent + tracestate
```

Outbox publisher chỉ đọc snapshot trong row. Nó không đọc `AsyncLocal` hoặc `Activity.Current` lúc publish;
nếu process restart hay message nằm pending nhiều phút, identity vẫn không đổi.

## Causation tại consumer boundary

Kafka consumer trước hết đối chiếu envelope với identity headers. `KafkaMessageIdentityScope` sau đó tạo
identity cho business effect kế tiếp:

```text
OrderPlacedV1
  MessageId     = M1
  CorrelationId = C1
  CausationId   = null

Barista/Kitchen xử lý M1
  Current.CorrelationId = C1
  Current.CausationId   = M1

OrderItemPreparedV1
  MessageId     = M2 / M3
  CorrelationId = C1
  CausationId   = M1

Counter xử lý M2
  Current.CorrelationId = C1
  Current.CausationId   = M2
```

Consumer scope được dispose dù handler thành công, throw hay bị cancel. Vì mỗi business event mới lấy identity
từ scope, Barista/Kitchen không còn nhận correlation/causation dưới dạng các string parameter dễ truyền sai.

## Kafka headers và contract validation

Outbox transport nhận cả envelope lẫn row identity, kiểm correlation/causation trùng nhau rồi mới serialize.
Publisher thêm `traceparent` và optional `tracestate`; consumer validate trace context trước khi mở scope.

Các header identity bắt buộc phải xuất hiện đúng một lần. Confluent Kafka cho phép duplicate header, vì vậy
chỉ đọc “giá trị cuối” sẽ tạo ambiguity giữa logger, proxy và consumer. Duplicate hoặc mismatch được xem là
permanent contract failure và đi theo retry/DLT discipline của Lesson 26. Retry router vẫn giữ nguyên key,
bytes, `MessageId`, correlation, causation và trace headers; nó không tạo một business event mới.

Ở phía producer, envelope/row identity hoặc trace snapshot hỏng là immutable Outbox corruption: retry không
thể tự sửa dữ liệu đó. Publisher đặt `RejectedAtUtc`, giải phóng lease, ghi safe code `invalid-contract` và
log một operational error không chứa payload. Claim query bỏ qua row đã reject; chỉ lỗi transport không xác
định mới dùng `publish-failed` cùng backoff. Row vẫn nằm trong database để operator điều tra thay vì bị xóa.

## Logs và SignalR

HTTP request log có `CorrelationId` trùng response header. Kafka handler log scope chỉ chứa metadata bounded:
event type/version, topic, partition, offset, message/correlation/causation ID và delivery attempt. Payload,
loyalty identity, authorization và broker credential không được log.

`OrderUpdateMessage` thêm `correlationId` và nullable `causationId`. Notification phát trong root request dùng
root identity; notification phát khi Counter consume prepared event dùng prepared-event `MessageId` làm direct
causation. Hai field mới là additive nên SignalR client cũ vẫn đọc được các field trước đó; TypeScript client
được cập nhật type để code mới không bỏ quên identity.

## Verification

Chạy các proof tập trung:

```bash
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj \
  -c Release --filter 'FullyQualifiedName~CorrelationTests|FullyQualifiedName~OrderUpdateBroadcastTests|FullyQualifiedName~StructuredLoggingTests'

dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj \
  -c Release --filter 'FullyQualifiedName~Correlation|FullyQualifiedName~KafkaTransportTests'

dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj \
  -c Release --filter FullyQualifiedName~CorrelationContinuityTests
```

`CorrelationContinuityTests` dùng PostgreSQL và Kafka thật để chứng minh root row, hai station rows và
notifications cùng correlation; root causation là `null`; hai event con trỏ về root `MessageId`; trace
snapshot không đổi. Fresh Compose smoke lấy correlation từ HTTP response rồi đối chiếu trực tiếp ba module
Outbox:

```bash
tests/scripts/phase-3-smoke-tests.sh
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka api signalr-client
./scripts/phase-3-smoke.sh
docker compose down --volumes --remove-orphans
```

## Summary kiến thức

- `MessageId`, `CorrelationId`, `CausationId` và W3C trace identity giải quyết các bài toán khác nhau.
- Root correlation phải được composition boundary tạo và sở hữu; không tin arbitrary client header.
- Root event có causation `null`; business event mới kế thừa correlation và trỏ tới inbound `MessageId`.
- Retry là transport movement nên giữ nguyên message/correlation/causation, không tạo identity mới.
- `AsyncLocal` chỉ an toàn khi scope được nest/dispose rõ ràng và không dùng làm persistent storage.
- Outbox writer snapshot identity trong business transaction; publisher background không đọc ambient context.
- Envelope/row/header duplication cần đối chiếu; duplicate header cũng là ambiguous contract input.
- Immutable Outbox corruption phải được quarantine; retry chỉ dành cho lỗi có khả năng tự hồi phục.
- Trace context phải được validate và propagate, nhưng tracing instrumentation/activity mới thuộc Lesson 29.
- Structured log dùng metadata allow-list; không log payload, loyalty identity, token hoặc credential.
- SignalR notification cũng là observability surface và cần mang workflow/direct-cause identity.
- End-to-end proof nên kiểm giá trị continuity thật, không chỉ kiểm field hoặc header có tồn tại.
