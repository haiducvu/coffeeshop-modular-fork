# Lesson 22 — Kafka JSON transport

Lesson 21 đã định nghĩa integration contract nhưng chưa chọn cách vận chuyển. Bài này thêm một messaging
port độc lập với broker và một adapter Kafka dùng JSON. Workflow đặt món vẫn chạy hoàn toàn như Lesson 21;
Kafka mới là hạ tầng opt-in để học transport, chưa thay thế domain event trong process.

## Mục đích bài học

Business module không nên biết `Confluent.Kafka`, topic hay consumer group. Hai interface trong
`CoffeeShop.Messaging.Abstractions` tạo ranh giới đó:

- `IIntegrationEventPublisher` nhận key và envelope để gửi message;
- `IIntegrationEventHandler<TPayload>` nhận envelope cùng delivery context để xử lý message.

`CoffeeShop.Messaging.Kafka` triển khai các port, còn API chỉ đóng vai trò composition root. Architecture
tests bảo đảm Abstractions không phụ thuộc adapter, adapter không phụ thuộc business module, và module
không tham chiếu Kafka.

## Topic, key, partition và consumer group

Topic được resolve từ semantic event identity thay vì CLR type name:

| Integration event | Kafka topic |
| --- | --- |
| `coffeeshop.order-placed:1` | `<prefix>.orders.v1` |
| `coffeeshop.order-item-prepared:1` | `<prefix>.preparation.v1` |

Publisher yêu cầu message key. Với order event, key là `OrderId`; Kafka hash cùng key vào cùng partition,
nhờ đó các event của một order giữ thứ tự trong partition. Kafka không cam kết thứ tự giữa nhiều partition.

Consumer group có dạng `<group-prefix>.<consumer-role>`. Các instance cùng group chia nhau partition để
scale ngang; consumer role khác dùng group khác nên mỗi vai trò vẫn nhận riêng một bản message.

## JSON value và Kafka headers

Value là UTF-8 JSON của `IntegrationEventEnvelope<TPayload>` với camel-case chính xác. Deserializer bỏ qua
field mới chưa biết, nên producer có thể thêm field tương thích mà consumer V1 cũ vẫn đọc được. Ngược lại,
`eventType`, `eventVersion` và payload sai vẫn bị từ chối.

Các identity quan trọng được lặp lại trong header: message ID, event type/version, occurred-at, correlation,
causation và `content-type=application/json`. Mapper đối chiếu header với envelope khi đọc. Việc lặp này cho
phép router/observability xem metadata mà không parse payload, đồng thời ngăn header và body mô tả hai
message khác nhau.

## Delivery policy

Producer dùng `acks=all` và bật idempotent producer. Broker chỉ xác nhận sau khi toàn bộ in-sync replica đã
ghi message, còn producer có thể retry mà không tạo bản sao do retry trong cùng producer session.

Consumer tắt auto commit và chỉ commit offset sau khi handler hoàn tất. Nếu process dừng trước commit,
message sẽ được giao lại; vì vậy semantic vẫn là at-least-once, không phải exactly-once. Khi host shutdown,
worker nhận cancellation và gọi `Close()` để rời consumer group sạch. Retry, dead-letter và business
idempotency được bổ sung ở các lesson sau.

## Test với broker thật

Unit tests khóa topic mapping, JSON compatibility, header consistency và client policy. Integration test dùng
Testcontainers khởi động Kafka KRaft thật, publish/consume JSON, dừng host, rồi khởi động lại cùng consumer
group. Host thứ hai chỉ nhận message mới, chứng minh offset của message đầu đã được commit và consumer đóng
sạch.

Compose cung cấp Kafka 4.1.1 trong profile `messaging`; mặc định Kafka vẫn tắt:

```bash
docker compose --profile messaging up -d kafka

KAFKA_ENABLED=true \
docker compose --profile messaging up -d --build postgres redis kafka api
```

Kafka chỉ tham gia `/health/ready` khi `Messaging:Kafka:Enabled=true`. Broker hỏng làm readiness trả `503`,
nhưng `/health/live` vẫn `200` vì process còn sống. Health response không lộ bootstrap server hay exception.

## Verification

Chạy unit và integration test dành cho transport:

```bash
dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj -c Release
dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj -c Release
```

Sau đó kiểm tra toàn solution và behavior cũ:

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx -c Release --no-restore
dotnet test CoffeeShop.slnx -c Release --no-build

docker compose up -d --build postgres redis api signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
```

## Summary kiến thức

- Ports giữ business code độc lập với Kafka; adapter và composition root sở hữu chi tiết hạ tầng.
- Topic biểu diễn event stream có version; key quyết định partition và phạm vi ordering.
- Consumer group quyết định message được chia tải hay fan-out cho một vai trò khác.
- JSON có thể tiến hóa bằng additive fields, nhưng semantic identity và header/body phải nhất quán.
- `acks=all` cộng idempotent producer bảo vệ producer retry; chúng không tạo exactly-once business workflow.
- Commit offset sau handler tạo at-least-once delivery, nên consumer idempotency vẫn là yêu cầu bắt buộc.
- Readiness mô tả khả năng phục vụ hiện tại; liveness chỉ mô tả process còn sống.
