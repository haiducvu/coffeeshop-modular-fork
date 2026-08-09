# Bài 07: Domain event trong process

## Mục tiêu

Cho `Order` ghi lại những sự kiện nghiệp vụ vừa xảy ra và dispatch chúng sau khi state đã được lưu, nhưng không làm Domain phụ thuộc MediatR, EF Core hoặc Kafka.

## Luồng mới

```text
Order.Place
    │ tạo LineItem + RaiseDomainEvent
    ▼
Order.DomainEvents: OrderItemAccepted[]
    │
    ▼
EfOrderRepository.SaveChangesAsync
    ├── 1. PostgreSQL commit
    ├── 2. IDomainEventDispatcher.DispatchAsync
    │          │
    │          ▼
    │   DomainEventNotification<OrderItemAccepted>
    │          │
    │          ▼
    │      MediatR IPublisher
    └── 3. ClearDomainEvents khi dispatch thành công
```

Mỗi line item được Order chấp nhận tạo đúng một `OrderItemAccepted` gồm Order ID, LineItem ID, ItemType và PreparationStation. Event diễn tả một fact đã xảy ra, không phải mệnh lệnh yêu cầu Order làm gì.

## Aggregate event collection

`AggregateRoot` sở hữu collection event private. Aggregate chỉ thêm event qua `RaiseDomainEvent`; caller bên ngoài chỉ đọc snapshot qua `IReadOnlyCollection`. Persistence boundary được phép gọi `ClearDomainEvents` sau khi dispatch hoàn tất.

Event được tạo cùng lúc với state transition trong `Order.Place`. Nếu event được dựng ở endpoint hoặc repository, nó có thể bị quên khi một adapter khác gọi domain theo con đường mới.

`OrderItemAccepted` chỉ implement `IDomainEvent` do Domain sở hữu. Nó không implement `INotification`, nhờ vậy Domain vẫn framework-free.

## Adapter MediatR

Application định nghĩa port `IDomainEventDispatcher` và typed wrapper `DomainEventNotification<TDomainEvent>`. Infrastructure adapter tạo đúng closed generic wrapper từ runtime event type rồi gọi `IPublisher.Publish`.

Application handler ở các bài sau có thể implement:

```csharp
INotificationHandler<DomainEventNotification<OrderItemAccepted>>
```

MediatR notification có thể có nhiều handler và chỉ chạy trong process hiện tại. Nó không lưu message, không retry sau process crash và không phải Kafka.

## Thứ tự save rồi dispatch

Repository thu event từ các aggregate đang được EF Core track, gọi `SaveChangesAsync` trước, sau đó mới dispatch. Integration test query PostgreSQL ngay trong dispatcher để chứng minh state đã tồn tại trước khi consumer chạy.

Event chỉ được clear sau khi dispatcher trả về thành công. Vì vậy một lần gọi save tiếp theo không dispatch trùng event đã xử lý. Nếu dispatcher ném exception, collection còn lại để failure không bị che giấu.

## Dual-write gap cố ý

Luồng hiện tại có hai hành động tách biệt:

1. Commit database thành công.
2. Dispatch event trong memory.

Process có thể chết giữa hai bước: Order đã lưu nhưng handler không chạy. Đảo thứ tự cũng không giải quyết được vì handler có thể chạy rồi database rollback. Đây là dual-write gap; Phase Kafka sẽ dùng transactional outbox thay vì tuyên bố delivery đáng tin cậy quá sớm.

## Chu trình TDD

1. Domain tests fail compile vì chưa có event type và aggregate collection.
2. Integration tests fail compile vì chưa có dispatcher port, MediatR adapter và repository constructor mới.
3. Domain implementation tối thiểu làm test event payload/clear xanh.
4. Repository capture event, save PostgreSQL, dispatch và clear làm post-save test xanh.
5. Adapter test chứng minh framework-free event được bọc thành typed MediatR notification.
6. Full suite phát hiện ba class khởi động ba PostgreSQL container song song; collection fixture dùng chung một container loại bỏ Resource Reaper race.

## Chạy bài học

```bash
dotnet test tests/CoffeeShop.DomainTests/CoffeeShop.DomainTests.csproj
dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj
dotnet test CoffeeShop.slnx
```

Docker phải chạy cho PostgreSQL integration tests.

## Kiến thức cần nhớ

- Domain event là fact nội bộ của domain; integration event là public contract giữa process/service.
- Aggregate phải raise event tại cùng nơi state thay đổi.
- Domain không cần biết cơ chế dispatch.
- Save-before-dispatch cho handler thấy state đã commit nhưng vẫn có dual-write gap.
- Chỉ clear event sau dispatch thành công để không âm thầm mất failure.
- In-process notification không durable, không thay thế broker và outbox.
- Fixture đắt tiền có thể dùng chung ở cấp test collection, nhưng test data vẫn phải dùng ID riêng để tránh phụ thuộc thứ tự.

## Sai lầm thường gặp

- Cho domain event implement interface của MediatR hoặc Kafka SDK.
- Raise event trong controller thay vì aggregate.
- Clear event trước khi dispatcher hoàn tất.
- Publish trước khi transaction commit rồi để consumer đọc state chưa tồn tại.
- Gọi luồng hiện tại là reliable hoặc exactly-once.
- Serialize domain event trực tiếp làm integration contract public.

## Bài tập

1. Đặt exception trong dispatcher và quan sát `DomainEvents` chưa bị clear.
2. Thêm handler ghi lại `OrderItemAccepted` rồi đăng ký qua Application assembly scanning.
3. Giải thích crash ở từng điểm trước save, sau save và sau dispatch gây hậu quả gì.
4. Phác thảo một bảng Outbox chứa event cùng transaction với Order.

## Technical debt cố ý

- Database commit và event dispatch chưa atomic; Outbox ở Lesson 23 sẽ đóng khoảng trống này.
- Adapter memory của API tests chỉ phục vụ HTTP behavior và chưa dispatch domain events.
- Chưa có handler nghiệp vụ cho `OrderItemAccepted`; Lesson 8 thêm Barista workflow đầu tiên.

Bài 08 xử lý các item của Barista bất đồng bộ với delay và time có thể test deterministically.
