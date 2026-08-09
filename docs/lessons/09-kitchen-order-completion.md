# Bài 09: Kitchen workflow và hoàn tất Order

## Mục tiêu

Xử lý food items bằng Kitchen, đưa prepared event quay lại Order aggregate và chuyển Order sang Fulfilled đúng một lần khi mọi line item đã hoàn tất.

## Event flow

```text
OrderItemAccepted(Kitchen)
          ▼
HandleKitchenOrderItemAccepted
  ├── delay 5/7/3 giây
  ├── persist kitchen.items
  └── OrderItemPrepared
              ▼
      HandleOrderItemPrepared
              ▼
      Order.CompleteItem
        ├── duplicate → no-op
        ├── partial → Order InProgress
        └── all done → Order Fulfilled
              ▼
          OrderUpdated
```

## Timing gốc của Kitchen

| Item | Delay |
|---|---:|
| Cake Pop | 5 giây |
| Croissant / Chocolate Croissant / Muffin | 7 giây |
| Fallback | 3 giây |

Kitchen dùng chung `IPreparationDelay` ở `Application.Common.Time`. Port được chuyển ra Common vì cả Barista và Kitchen cùng cần capability chờ, nhưng mỗi module vẫn sở hữu policy riêng.

## State machine trong aggregate

`Order.CompleteItem` là nơi duy nhất đổi `LineItem.Status` và `Order.Status`:

- Line ID không thuộc Order: ném `DomainException`.
- Item đã Fulfilled: trả `false`, không tạo event mới.
- Item vừa hoàn tất: trả `true` và raise một `OrderUpdated`.
- Khi tất cả item Fulfilled: Order chuyển sang Fulfilled.

Idempotency ở domain giúp duplicate delivery không tạo thêm save/event. Nó chưa đủ cho distributed exactly-once, nhưng là nền tảng bắt buộc khi broker về sau có at-least-once delivery.

## Optimistic concurrency

Hai prepared events có thể tải cùng một Order rồi hoàn tất hai line item khác nhau. Nếu cả hai ghi đè toàn aggregate mà không phát hiện stale state, update đầu tiên có thể mất.

`Order.Version` là concurrency token. Mỗi state transition tạo version mới; EF đưa old version vào điều kiện `UPDATE`. Context thứ hai dùng version cũ nhận `DbUpdateConcurrencyException`, adapter chuyển thành `OrderConcurrencyException` của Application và clear stale tracking state.

Handler retry tối đa ba lần: tải aggregate mới, apply event idempotently và save lại. Integration test dùng hai DbContext thật để chứng minh stale completion bị từ chối thay vì silently overwrite.

## Repository ownership

`EfOrderRepository` chỉ thu domain events từ `Order`, không thu mọi `AggregateRoot` trong DbContext. Đây là chi tiết quan trọng khi dispatch lồng nhau:

1. Barista repository đang dispatch `OrderItemPrepared`.
2. Order handler save Order trong cùng scope.
3. Order repository không được publish lại event còn pending của Barista item.

Nếu repository quét tất cả aggregate, prepared event có thể tự dispatch đệ quy.

## Chu trình TDD

1. Domain tests fail compile vì chưa có `CompleteItem`/`OrderUpdated`.
2. Application tests fail compile vì Kitchen module và prepared handler chưa tồn tại.
3. Partial, full, duplicate và unknown-item cases làm state machine tối thiểu xanh.
4. Kitchen timing tests xanh bằng fake delay/clock, không sleep thật.
5. Concurrency integration test RED với `PendingModelChangesWarning` trước migration.
6. Migration `AddKitchenAndOrderConcurrency` làm PostgreSQL concurrency test xanh.

## Chạy bài học

```bash
dotnet test tests/CoffeeShop.DomainTests/CoffeeShop.DomainTests.csproj \
  --filter FullyQualifiedName~OrderCompletionTests
dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj \
  --filter FullyQualifiedName~OrderConcurrencyTests
dotnet test CoffeeShop.slnx
```

## Kiến thức cần nhớ

- Aggregate sở hữu state transition và idempotency rule.
- Duplicate event không nên tạo duplicate side effect.
- Optimistic concurrency phát hiện lost update; retry phải reload state mới.
- Provider exception được dịch sang exception thuộc Application boundary.
- Mỗi repository chỉ dispatch event của aggregate type nó sở hữu.
- Fake time/delay giữ workflow tests nhanh và deterministic.

## Sai lầm thường gặp

- Handler sửa public setters trên LineItem/Order.
- Duplicate completion vẫn save và publish event mới.
- Catch concurrency exception rồi retry cùng tracked entity cũ.
- Dùng lock trong một process để giả quyết concurrency từ nhiều instance.
- Repository quét mọi aggregate trong shared DbContext và gây recursive dispatch.

## Bài tập

1. Đảo thứ tự Kitchen/Barista completion và chứng minh kết quả cuối giống nhau.
2. Bỏ `IsConcurrencyToken`, chạy integration test và quan sát lost-update không còn bị phát hiện.
3. Làm fake repository throw concurrency hai lần rồi xác nhận handler thử lần ba.
4. Thêm prepared event trùng và chứng minh `OrderUpdated` chỉ xuất hiện một lần.

## Technical debt cố ý

- Retry chưa có jitter/metric và chỉ phù hợp conflict ngắn trong process.
- Prepared events vẫn không durable; Outbox/Inbox sẽ xuất hiện ở Phase Kafka.
- Client chưa nhận update realtime; Lesson 10 thêm typed SignalR contract.

Bài 10 stream `OrderUpdated` bằng SignalR và cung cấp client TypeScript có reconnect lifecycle.
