# Lesson 23 — Transactional Outbox

Lesson 22 đã có Kafka transport nhưng Counter chưa gửi business event qua broker. Nếu handler ghi order vào
PostgreSQL rồi gọi Kafka trực tiếp, hai hệ thống độc lập tạo ra dual-write: database có thể commit trong khi
Kafka lỗi, hoặc Kafka nhận message nhưng database rollback. Bài này loại bỏ khoảng mất message đó bằng cách
lưu order và integration event vào cùng một PostgreSQL transaction.

## Mục đích bài học

Khi một order được chấp nhận, `PlaceOrderHandler` tạo đúng một `OrderPlacedV1` chứa toàn bộ stable line-item
ID cùng tên item/station có nghĩa. `ICounterOutboxWriter` biến payload thành envelope và track một
`CounterOutboxMessage` trên chính `CounterDbContext` mà repository đang dùng.

Handler chỉ gọi một `SaveChangesAsync`. Theo transaction mặc định của EF Core, order, line items và Outbox
row hoặc cùng commit, hoặc cùng rollback. Không cần mở transaction thủ công vì toàn bộ thay đổi nằm trong
một lần save trên một context.

```text
PlaceOrderHandler
  ├─ track Order + LineItems
  ├─ track CounterOutboxMessage
  └─ SaveChangesAsync()  ← một PostgreSQL transaction
```

## Dữ liệu Outbox

Migration tạo `counter.outbox_messages`, cùng schema với aggregate mà Counter sở hữu. Row chứa:

- message ID, semantic event type/version và thời điểm xảy ra;
- canonical envelope JSON trong cột `jsonb`;
- correlation/causation và W3C trace parent/state;
- attempt count, thời điểm retry tiếp theo;
- lease ID/expiry, published time và safe bounded error code.

Các field scheduling/lease chưa được sử dụng ở bài này nhưng schema đã chuẩn bị cho publisher cạnh tranh an
toàn ở Lesson 24. Index `(PublishedAtUtc, NextAttemptAtUtc)` phục vụ tìm pending rows; index lease expiry phục
vụ reclaim lease hết hạn.

Canonical JSON là định dạng persistence nội bộ của Outbox, không phải cam kết Kafka luôn dùng JSON. Lesson 28
có thể đổi wire encoding sang Avro mà những row cũ vẫn chứa logical integration contract có thể đọc được.

## Data minimization và identity

Payload chỉ chứa `OrderId`, `LineItemId`, `ItemType` và `Station`. Loyalty member ID, location, source, giá và
HTTP request không được sao chép sang Outbox. PostgreSQL test kiểm tra trực tiếp JSON để khóa quy tắc này.

Mỗi row có một `MessageId` mới. Trước khi Lesson 27 định nghĩa correlation từ HTTP, canonical UUID của chính
message ID được dùng làm correlation ID ban đầu; causation để trống. Nếu request đang có W3C Activity,
`traceparent` và `tracestate` được lưu để publisher sau này tiếp tục trace qua ranh giới database/broker.

## Failure semantics

Integration test cố ý enqueue một event type dài hơn giới hạn `varchar(128)`. PostgreSQL từ chối Outbox row;
vì order và row nằm trong cùng `SaveChangesAsync`, transaction rollback và database không còn cả order lẫn
event. Đây là bằng chứng chạy được cho local atomicity, không chỉ là nhận định từ code review.

Outbox mới chỉ ngăn mất ý định publish. Bài này chưa có polling worker, lease hay Kafka publish, nên pending
rows vẫn nằm trong database. Domain event handlers trong process tiếp tục chạy sau khi transaction commit;
workflow fulfillment và API behavior vì thế giữ nguyên như Lesson 22.

## Verification

Chạy atomicity và schema tests trên PostgreSQL thật:

```bash
dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj \
  -c Release \
  --filter 'FullyQualifiedName~CounterOutboxAtomicityTests|FullyQualifiedName~ModuleSchemaTests'
```

Kiểm tra migration SQL và toàn repository:

```bash
dotnet ef migrations script \
  --project src/CoffeeShop.Modules.Counter/CoffeeShop.Modules.Counter.csproj \
  --context CounterDbContext

dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx -c Release --no-restore
dotnet test CoffeeShop.slnx -c Release --no-build
```

## Summary kiến thức

- Dual-write xuất hiện khi một use case phải commit độc lập vào database và broker.
- Transactional Outbox lưu business state và ý định publish trong một local database transaction.
- Một `DbContext` và một `SaveChangesAsync` đủ tạo atomicity khi provider hỗ trợ transaction.
- Canonical Outbox JSON tách logical contract khỏi Kafka wire encoding.
- Payload tối thiểu giảm coupling và tránh nhân bản dữ liệu khách hàng không cần thiết.
- Pending/scheduling/lease fields tạo nền cho at-least-once publisher nhưng chưa tự publish message.
- Outbox giải quyết producer-side message loss; consumer duplicate vẫn cần Inbox ở Lesson 25.
