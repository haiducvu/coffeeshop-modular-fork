# Lesson 25 — Idempotent Inbox và Kafka fulfillment

Lesson 24 đã publish Counter Outbox sang Kafka nhưng vẫn giữ fulfillment in-process. Bài này thực hiện một
atomic cutover: Kafka trở thành đường business thật, Barista/Kitchen nhận `OrderPlacedV1`, còn Counter nhận
`OrderItemPreparedV1`. Inbox trong từng module biến duplicate delivery thành successful no-op.

## Mục đích bài học

Kafka chỉ cung cấp at-least-once delivery cho workflow này. Consumer có thể nhận lại record nếu process dừng
sau database commit nhưng trước offset commit. Nếu handler tạo item hoặc hoàn tất line item mỗi lần được gọi,
redelivery sẽ lặp business side effect.

Mỗi consumer role dùng một group riêng:

- `barista` và `kitchen` cùng nhận `OrderPlacedV1` nhưng chỉ xử lý item thuộc station của mình;
- `counter` nhận `OrderItemPreparedV1` và hoàn tất line item tương ứng;
- offset chỉ được commit sau khi module handler trả về thành công.

## Module-local Inbox transaction

Ba schema đều có `inbox_messages` với composite primary key `(HandlerName, MessageId)`. Một delivery mới track
Inbox row, business state và outgoing Outbox trên cùng module `DbContext`, sau đó gọi đúng một
`SaveChangesAsync`:

```text
Kafka record
  └─ Begin Inbox
      ├─ track Inbox row
      ├─ mutate module-owned state
      ├─ track outgoing Outbox row
      └─ Complete Inbox + SaveChangesAsync
             └─ một PostgreSQL transaction
```

Delivery đã tồn tại trả `Duplicate` và không chạy side effect. Nếu hai delivery cạnh tranh cùng lúc, unique
constraint chọn một winner; transaction loser rollback toàn bộ item/Outbox rồi cũng được xem là duplicate
thành công. Mọi database failure khác vẫn được ném ra, nên Kafka offset không được commit.

Thiết kế dùng transaction mặc định của một `SaveChangesAsync`, thay vì tự mở transaction quanh nhiều lần save.
Nhờ vậy local atomicity tương thích với EF Core execution strategy đang bật và không giữ transaction trong
thời gian mô phỏng preparation delay.

## Outbox chain giữa các module

Counter tiếp tục ghi `OrderPlacedV1` cùng order. Barista/Kitchen tạo item, hoàn tất preparation và ghi
`OrderItemPreparedV1` trong cùng transaction với Inbox của record đã tiêu thụ. Hai Outbox publisher mới dùng
bounded batch, lease và `FOR UPDATE SKIP LOCKED` giống Lesson 24.

Result envelope:

- giữ nguyên `OrderId` và stable `LineItemId`;
- dùng symbolic `ItemType` và `Station`;
- giữ `MadeBy` cùng completion time;
- giữ correlation ID của order event và dùng consumed message ID làm causation ID.

Correlation/trace propagation đầy đủ sẽ được quan sát hóa ở Lesson 27 và Lesson 29; Lesson 25 chỉ giữ identity
trong persistence/envelope để không phải đổi contract sau cutover.

## Atomic composition cutover

Registration của `HandleBaristaOrderItemAccepted`, `HandleKitchenOrderItemAccepted` và Counter
`HandleOrderItemPrepared` được tháo khỏi composition. Các class cũ vẫn tồn tại để những commit trước còn dễ
đối chiếu, nhưng released Lesson 25 không đăng ký đồng thời old/new business paths.

Các local effect vẫn được giữ:

- `OrderUpdated` tiếp tục được dispatch sau Counter database commit;
- SignalR publisher tiếp tục phát typed update;
- fulfilled-order Redis cache tiếp tục invalidation khi order hoàn tất;
- HTTP `/v1` và `/v2` contracts không đổi, chỉ chuyển sang eventual fulfillment qua Kafka.

Kafka trở thành dependency mặc định của Compose và tham gia readiness. API tests trong environment `Testing`
vẫn không khởi động external broker.

Fresh broker có thể healthy trước khi topic hoặc consumer-group coordinator sẵn sàng. Consumer adapter không
coi `ConsumeException` non-fatal trong cửa sổ startup là business failure: nó log warning, đợi có giới hạn rồi
retry; lỗi fatal vẫn thoát worker. Metadata topic được refresh mỗi giây để consumer sớm thấy topic do producer
tạo sau `Subscribe`. Log native của producer/consumer được forward qua `ILogger`, nhờ vậy stdout của API vẫn
là newline-delimited JSON như Lesson 20.

## Failure và duplicate semantics

```text
DB commit thành công ── process crash ── offset chưa commit
        │
        └─ Kafka redelivery
              └─ Inbox key đã tồn tại
                    └─ no-op thành công + commit offset
```

Inbox không tạo exactly-once transport. Nó chỉ làm business handler idempotent trong ranh giới module. Nếu
business transaction lỗi, Inbox insert, item change và outgoing Outbox cùng rollback; record vẫn chưa được
acknowledge. Lesson 26 sẽ bổ sung retry topics và DLT để consumer failure không làm hosted worker dừng lâu dài.

## Verification

Chạy duplicate/atomicity tests trên PostgreSQL thật và workflow trên Kafka thật:

```bash
dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj \
  -c Release --filter FullyQualifiedName~InboxIdempotencyTests

dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj \
  -c Release --filter FullyQualifiedName~KafkaFulfillmentWorkflowTests
```

Kiểm tra smoke behavior rồi chạy fresh Compose workflow:

```bash
tests/scripts/phase-3-smoke-tests.sh
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka api signalr-client
./scripts/phase-3-smoke.sh
docker compose down --volumes --remove-orphans
```

Smoke dùng deadline toàn cục, yêu cầu Kafka readiness, đặt một mixed order, chờ fulfillment, kiểm Redis cache,
Inbox counts, station effects và không còn pending Outbox row. Khi authentication bật, script lấy customer
token với OpenID scope, đọc `sub` qua userinfo để dùng đúng loyalty-member identity, rồi đi qua protected `/v2`
order API. Sau fulfillment, legacy read model vẫn được gọi để chứng minh Redis cache behavior không đổi.

## Summary kiến thức

- At-least-once delivery bắt buộc consumer business phải idempotent.
- Inbox key phải bao gồm handler identity và message identity.
- Inbox, business state và outgoing Outbox phải commit trong cùng local transaction.
- Một `SaveChangesAsync` trên một `DbContext` tạo atomicity và tương thích execution strategy.
- Unique constraint xử lý cả duplicate tuần tự và concurrent race.
- Kafka offset chỉ được commit sau database success; failure/cancellation không acknowledge record.
- Lỗi Kafka startup non-fatal cần retry; lỗi fatal và business exception vẫn phải nổi lên.
- Topic metadata refresh quyết định consumer thấy topic được tạo muộn nhanh đến đâu.
- Consumer groups tách vai trò Barista, Kitchen và Counter dù cùng chạy trong một process.
- Stable line-item ID nối chính xác preparation result về Counter aggregate.
- Atomic cutover tránh chạy đồng thời in-process path và Kafka path.
- Inbox tạo effectively-once business effect, không biến transport thành end-to-end exactly-once.
