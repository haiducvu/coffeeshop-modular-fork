# Lesson 26 — Bounded retry và Dead-Letter Topic

Lesson 25 làm business effect idempotent bằng Inbox, nhưng một record lỗi vẫn có thể làm consumer lặp vô hạn
hoặc dừng worker. Bài này thêm retry budget hữu hạn, phân loại lỗi transient/permanent và giữ poison message
trong Dead-Letter Topic (DLT) để xử lý có kiểm soát.

## Mục đích bài học

Retry không phải đáp án cho mọi failure. Hạ tầng tạm mất kết nối có thể hồi phục sau một khoảng ngắn, còn JSON
sai, version không hỗ trợ hoặc business value không hợp lệ sẽ không tự tốt lên dù chạy lại bao nhiêu lần.
Lesson 26 tạo policy rõ ràng:

```text
original delivery
  ├─ transient ──> {topic}.retry.1 ── 1 giây ──> handler
  │                    └─ transient ──> {topic}.retry.2 ── 5 giây ──> handler
  │                                           └─ transient ──> {topic}.dlt
  └─ permanent ──────────────────────────────────────────────> {topic}.dlt
```

Ba logical attempt là `original`, `retry.1` và `retry.2`. Đây là retry budget, không phải lời hứa
exactly-three physical deliveries: process vẫn có thể crash giữa lúc publish retry/DLT thành công và commit
offset nguồn. Inbox từ Lesson 25 bảo vệ business effect khi crash-window đó gây duplicate.

## Failure classifier

`IIntegrationFailureClassifier` trả về hai dữ kiện nhỏ, an toàn để đi qua transport:

- `Transient`: failure hạ tầng/concurrency chưa biết chắc là vĩnh viễn; dùng code `processing-transient`;
- `Permanent`: JSON/contract sai, version không hỗ trợ, header hoặc business input không hợp lệ;
- `OperationCanceledException`: lifecycle control, không đi qua classifier, không forward và không commit.

Module báo một domain-invalid integration message bằng `IntegrationEventRejectedException` cùng safe code. Ví
dụ Counter dùng `order-not-found` thay vì để `DomainException` rơi vào default transient policy. Database,
concurrency và failure chưa biết rõ tiếp tục là transient.

Classifier tuyệt đối không đưa `Exception.Message`, stack trace, connection string, credential hoặc token vào
Kafka header. Safe error code chỉ cho phép ký tự chữ/số và dấu `-`, tối đa 64 ký tự; code không hợp lệ được thay
bằng fallback allow-listed.

## Retry record giữ nguyên identity

Retry router copy nguyên key, payload bytes và các header business/contract đã có. Nó chỉ thêm hoặc cập nhật
transport metadata:

| Header | Ý nghĩa |
|---|---|
| `delivery-attempt` | Logical attempt sẽ xử lý record |
| `not-before` | Thời điểm retry consumer mới được dispatch |
| `original-topic` | Topic nơi failure đầu tiên được consume |
| `original-partition` / `original-offset` | Vị trí record gốc để điều tra |
| `failure-kind` / `failure-code` | Phân loại và safe error code |
| `failure-at` | Thời điểm classifier xử lý failure |

`message-id`, correlation, causation, content type, trace headers và payload bytes không đổi. Record không
deserialize được vẫn vào DLT dưới dạng bytes gốc; vì vậy operator còn dữ liệu để điều tra mà transport không
cần hiểu business schema.

Reserved retry headers không phải input đáng tin cậy. Adapter suy ra topic family từ consumer đã cấu hình,
ghi đè origin metadata ở lần failure đầu và chỉ copy allow-list contract/identity/trace headers. Header lạ như
authorization, credential hoặc connection data không được đưa sang retry/DLT. `not-before` chỉ được đọc trên
đúng retry topic và bị từ chối nếu vượt delay của stage, nên producer không thể dùng header để reroute hoặc
treo original consumer.

## Delay topic và consumer

Mỗi consumer role có ba poll loop riêng cho topic gốc, `.retry.1` và `.retry.2`, nhưng cả ba resolve cùng
`IIntegrationEventHandler<TPayload>`. Vì vậy một retry đang chờ `not-before` không chặn original traffic. Trước
khi dispatch retry record, adapter gọi `IRetryDelay`; production dùng `TimeProvider`, còn test dùng controlled
clock/recording delay nên không phải sleep một hoặc năm giây thật.

Di chuyển qua retry topic phá strict ordering so với record đến sau trên topic gốc. Workflow CoffeeShop chấp
nhận trade-off này vì line item có stable identity, station handlers idempotent và Counter không phụ thuộc thứ
tự hoàn tất giữa các item.

## Offset discipline

Thứ tự acknowledge là invariant quan trọng nhất:

```text
business success / Inbox duplicate
  └─ commit source offset

processing failure
  └─ await retry-or-DLT ProduceAsync ACK
       └─ commit source offset

forward failure / cancellation
  └─ không commit source offset
       └─ source được seek/redeliver hoặc nhận lại sau restart/rebalance
```

Nếu commit lỗi sau khi Kafka đã ACK record forward, cùng source có thể tạo thêm một retry/DLT record. Đây là
at-least-once forwarding; replay và DLT operation phải deduplicate bằng original `MessageId`.

## Cấu hình

Defaults dành cho learning environment nằm dưới `Messaging:Kafka:Retry`:

```json
{
  "FirstDelay": "00:00:01",
  "SecondDelay": "00:00:05",
  "MaxPollInterval": "00:05:00"
}
```

Compose cho phép override các giá trị này. Kafka session timeout được đặt rõ là 10 giây; startup validation yêu
cầu hai delay dương, delay thứ hai lớn hơn delay thứ nhất, và max poll interval tối thiểu 5 phút đồng thời dài
hơn retry delay. Khoảng 5 phút giữ nguyên safety window mặc định của Kafka cho cả thời gian chờ retry lẫn handler
xử lý một đơn nhiều món tuần tự; không được thu ngắn chỉ dựa trên retry delay.

## Verification

Chạy deterministic routing tests và real-broker proofs:

```bash
dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj \
  -c Release --filter FullyQualifiedName~Retry

dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj \
  -c Release --filter FullyQualifiedName~KafkaRetryAndDeadLetterTests
```

Integration suite chứng minh transient recovery, exhausted retry, real Counter domain rejection đi thẳng DLT,
forwarding-failure redelivery, cancellation không acknowledge và retry wait không block original consumer.
Adversarial tests còn chứng minh reserved header không spoof được route/delay và secret header không lọt vào
DLT. Fresh Compose smoke đặt một mixed order,
kiểm Inbox/Outbox/Redis, rồi gửi malformed JSON vào orders topic. Hai consumer group `barista` và `kitchen`
phải giữ record đó trong DLT:

```bash
tests/scripts/phase-3-smoke-tests.sh
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka api signalr-client
./scripts/phase-3-smoke.sh
docker compose down --volumes --remove-orphans
```

Quy trình inspect, sửa và replay an toàn nằm tại
[Kafka DLT replay runbook](../operations/kafka-dead-letter-replay.md).

## Summary kiến thức

- Retry budget phải hữu hạn và tách transient failure khỏi permanent failure.
- Delay topic tránh block topic gốc bằng một retry loop vô hạn trong handler.
- `TimeProvider` và injected delay làm retry test deterministic, nhanh và không flaky.
- Retry/DLT phải giữ nguyên key, raw bytes, envelope identity, correlation, causation và trace context.
- DLT metadata chỉ chứa safe code; exception message, stack trace và secret không được copy.
- Reserved transport headers phải được derive/validate, không được tin từ arbitrary producer input.
- Malformed record vẫn cần giữ raw bytes để điều tra/replay có kiểm soát.
- Kafka chỉ commit source offset sau business success, duplicate no-op hoặc forward ACK.
- Forward/commit crash-window giữ semantics at-least-once; Inbox và replay deduplication vẫn bắt buộc.
- Cancellation là lifecycle control, không phải processing failure.
- Retry topics đánh đổi strict ordering; domain handler phải idempotent và không lệ thuộc cross-topic order.
- Stage-specific poll loops giữ retry delay tách khỏi throughput của original topic.
- Max poll interval phải lớn hơn session timeout và retry wait để consumer không bị group eviction.
- DLT không phải thùng rác: cần ownership, root-cause fix, audit và runbook replay rõ ràng.
