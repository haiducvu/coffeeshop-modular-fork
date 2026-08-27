# Lesson 24 — Publish leased Outbox batches

Lesson 23 đã lưu order và ý định publish trong cùng PostgreSQL transaction, nhưng pending row vẫn chưa rời
database. Bài này thêm polling publisher để drain Counter Outbox sang Kafka theo mô hình at-least-once, đồng
thời giữ nguyên toàn bộ workflow fulfillment in-process của Phase 2.

## Mục đích bài học

`CounterOutboxWorker` liên tục tạo scope ngắn và yêu cầu publisher xử lý một batch giới hạn. Store claim các
row đến hạn bằng một PostgreSQL statement duy nhất, gán lease rồi commit trước khi Kafka I/O bắt đầu:

```text
PostgreSQL                        process                         Kafka
    │                               │                              │
    ├─ claim + lease batch ─────────┤                              │
    │  short transaction commits    ├─ publish outside DB tx ─────>│
    │<─ conditional mark published ─┤<─ broker ACK ────────────────┤
```

Kafka publication hiện là shadow path. Domain events và handlers Barista/Kitchen/Counter trong process vẫn
hoạt động, nên HTTP và SignalR behavior không đổi. Worker chỉ được đăng ký khi `Messaging:Kafka:Enabled=true`;
Kafka tắt thì Lesson 23 tiếp tục chỉ tạo pending rows.

## Claim cạnh tranh an toàn

Claim dùng Common Table Expression với `FOR UPDATE SKIP LOCKED`, `LIMIT` và một `UPDATE ... RETURNING` nguyên
tử. Nhiều worker có thể cạnh tranh cùng bảng mà không chờ row worker khác đang khóa. Mỗi batch có lease ID
riêng và thời điểm hết hạn:

- row đã publish không bao giờ được claim lại;
- row chưa đến `NextAttemptAtUtc` bị bỏ qua;
- lease còn hiệu lực loại row khỏi batch khác;
- lease hết hạn cho phép worker khác reclaim sau crash;
- `BatchSize` tạo backpressure và giới hạn lượng công việc mỗi vòng.

Transaction chỉ bao quanh statement claim. Không giữ database lock trong lúc chờ broker, tránh biến độ trễ
Kafka thành lock contention của PostgreSQL.

## Success, retry và ownership

Sau broker ACK, publisher chỉ set `PublishedAtUtc` nếu row vẫn thuộc đúng lease. Publish lỗi thì store tăng
`Attempts`, đặt `NextAttemptAtUtc`, giải phóng lease và chỉ lưu error code allow-listed `publish-failed`.
Exception text, broker detail và payload không đi vào database hoặc structured log, tránh rò rỉ dữ liệu.

Bốn tham số được bind và validate khi startup:

- `BatchSize`: 1–500;
- `PollInterval`: 10 ms–1 phút;
- `LeaseDuration`: 1 giây–10 phút;
- `RetryDelay`: 100 ms–10 phút.

`TimeProvider` điều khiển lease/retry để test không cần sleep.

## Crash window và at-least-once

Không thể atomically commit một PostgreSQL row và Kafka ACK nếu không có distributed transaction. Nếu process
crash sau Kafka ACK nhưng trước `MarkPublishedAsync`, row vẫn leased. Khi lease hết hạn, worker reclaim và
publish lại cùng `MessageId`.

Vì vậy Lesson 24 đảm bảo **at-least-once publication**: không mất pending intent, nhưng duplicate là behavior
hợp lệ. Real-broker test cố ý bỏ qua bước mark sau ACK, advance clock qua lease expiry rồi chứng minh Kafka
nhận lại cùng message ID. Lesson 25 sẽ thêm consumer Inbox để duplicate không lặp business side effect.

## Verification

Chạy các test trọng tâm:

```bash
dotnet test tests/CoffeeShop.ApplicationTests/CoffeeShop.ApplicationTests.csproj \
  -c Release --filter FullyQualifiedName~OutboxPublisherTests

dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj \
  -c Release --filter FullyQualifiedName~CounterOutboxLeaseTests

dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj \
  -c Release --filter FullyQualifiedName~CounterOutboxKafkaTests
```

Chạy toàn bộ green gate:

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx -c Release --no-restore
dotnet test CoffeeShop.slnx -c Release --no-build
npm --prefix src/CoffeeShop.SignalRClient ci
npm --prefix src/CoffeeShop.SignalRClient run build
docker compose config --quiet
docker compose build
```

Messaging Compose smoke dùng fresh volumes, bật Kafka, đặt order và đọc shadow record từ topic
`coffeeshop.orders.v1`; Phase 1/2 smoke đồng thời xác nhận HTTP fulfillment vẫn không đổi.

## Summary kiến thức

- Polling publisher biến pending Outbox rows thành Kafka records mà không tạo dual-write trong request.
- `FOR UPDATE SKIP LOCKED` phù hợp cho queue-like table có nhiều worker cạnh tranh.
- Lease giúp reclaim công việc sau crash; conditional update bảo vệ ownership cũ.
- Claim transaction phải ngắn và Kafka I/O phải nằm ngoài database transaction.
- Retry cần bounded configuration và safe error code, không lưu exception/payload tùy ý.
- Broker ACK trước database mark tạo duplicate window không thể tránh trong kiến trúc này.
- Outbox publisher cung cấp at-least-once, không phải exactly-once end-to-end.
- Shadow publication cho phép quan sát Kafka path trước khi cut over business workflow ở Lesson 25.
