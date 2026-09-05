# Distributed failure demo — Lesson 34

## Phạm vi và điều kiện

Chỉ dùng stack Docker Compose demo do bạn quản lý, với Kafka, API/Counter External và hai worker External,
ba database của Lesson 33. Không chạy trên production, Dapr embedded, stack bật authentication, hoặc cùng
traffic khác. Các lệnh chạy từ repository root, cần Docker Compose, `curl`, `jq`, Python 3 và .NET 10 SDK.
Python chỉ dùng standard library, không cần pip; timeout helper dùng POSIX process groups (macOS/Linux).

Không dùng `set -x`, không paste payload/loyalty IDs, connection strings hoặc environment vào báo lỗi.
Script chỉ stop/start service được chọn, không tự xóa volume và không reset Kafka offsets.

## 1. Fresh stack và finite batch

**Cảnh báo:** `down --volumes` dưới đây xóa database và Kafka offsets của demo, không thể phục hồi nếu chưa
backup. Bỏ bước xóa nếu đang giữ dữ liệu học; stack cũ phải đúng layout Lesson 33 và hết pending work.

```bash
docker compose --profile demo --profile identity --profile observability --profile dapr down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka schema-registry api barista-worker kitchen-worker signalr-client
DATAGEN_ORDER_COUNT=3 DATAGEN_SEED=34 ./scripts/phase-4-smoke.sh
```

Kỳ vọng: ba mixed orders Fulfilled, đúng effect ở từng owner, Outboxes drained, replay được cả hai station
consume mà không tăng effect hoặc retry/DLT offsets. Chạy lại cùng lệnh cũng phải pass (marker mới).
`DATAGEN_ORDER_COUNT=1..20`; seed là số nguyên không âm. Không dùng random DataGen đồng thời với smoke.

## 2. Ngắt từng worker rồi phục hồi

```bash
./scripts/phase-4-fault-demo.sh barista-worker
./scripts/phase-4-fault-demo.sh kitchen-worker
```

Mỗi lệnh gửi đúng một mixed order, query chứng minh Counter đã commit, stop worker trong tối đa hai giây
graceful shutdown, xác nhận orders consumer lag dương, start lại rồi đợi fulfillment và duplicate gate.
Worker còn lại không bị stop; API, PostgreSQL và Kafka tiếp tục chạy. Nếu worker xử lý quá nhanh để còn
backlog khi stop, demo fail trong deadline và khôi phục worker: không biến một lần không inject được
fault thành proof thành công. Không có guarantee thứ tự hoàn tất giữa hai station.

Default global deadline là 240 giây, tối đa 900; cleanup recovery budget mặc định 10 giây, tối đa 30.
Tổng runtime giới hạn bởi deadline + recovery budget + overhead lập lịch nhỏ:

```bash
SMOKE_TIMEOUT_SECONDS=360 SMOKE_RECOVERY_SECONDS=15 ./scripts/phase-4-fault-demo.sh kitchen-worker
```

Nếu thất bại, đọc stage (`preflight`, `backlog`, `recovery`, `fulfillment`, `duplicate-replay`). Cleanup cố
start lại worker đã stop; nếu Docker daemon không đáp ứng hoặc nhận SIGKILL thì cần phục hồi thủ công:

```bash
docker compose start barista-worker kitchen-worker
docker compose ps --status running --services
```

Đợi pending work hoàn tất trước khi chạy lại. Không xóa Inbox hoặc reset offsets để “chữa” test đỏ.

## 3. Retry và poison DLT là hai proof riêng

Worker downtime không ép handler ném lỗi. Dùng real Kafka Testcontainers tests của Lesson 26 để kiểm
transient handler failure, bounded retry, exhausted retry vào DLT và forward-before-commit:

```bash
dotnet test tests/CoffeeShop.Messaging.IntegrationTests -c Release \
  --filter FullyQualifiedName~KafkaRetryAndDeadLetterTests
```

Các handler gây lỗi chỉ nằm trong test, không có failure endpoint production. Các tests chạy broker
thật cô lập và kiểm attempt/effect, không phải sleep rồi grep log. Để kiểm poison message đi qua **hai
process worker thật** và giữ correlation/causation proof, chạy gate đã có:

```bash
./scripts/phase-4-kitchen-smoke.sh
```

Gate này gửi mixed order, rồi poison JSON lên orders topic, đợi cả hai station chuyển DLT. Record lỗi
là dữ liệu demo cố ý; không xóa DLT sau proof. Main Lesson 34 smoke có thể chạy sau đó vì so failure-offset
delta, không yêu cầu DLT toàn cục bằng 0.

## 4. Regression gates

Trên Kafka stack hiện tại (chạy tuần tự, không song song):

```bash
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
DATAGEN_ORDER_COUNT=1 docker compose --profile demo run --rm datagen
```

Phase 1/2 và .NET suite giữ proof cho `/v1`, `/v2`, SignalR và Redis. CI còn dựng fresh identity topology,
OTLP/Jaeger topology và Dapr embedded regression theo `.github/workflows/ci.yml`; mỗi profile có cleanup
riêng. Lesson 34 không đổi authentication hoặc messaging adapter để làm test pass.

## Tham khảo

- [Lesson 34: kiến thức và acceptance contract](../lessons/34-distributed-flow.md).
- [Service data ownership](../architecture/service-data-ownership.md).
- [Kafka console producer header format](https://github.com/apache/kafka/blob/4.1.1/tools/src/main/java/org/apache/kafka/tools/LineMessageReader.java).
- [Kafka offset inspection](https://github.com/apache/kafka/blob/4.1.1/tools/src/main/java/org/apache/kafka/tools/GetOffsetShell.java).
