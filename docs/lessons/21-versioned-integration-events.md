# Lesson 21 — Versioned integration events

Phase 3 bắt đầu bằng việc tách contract dùng để tích hợp khỏi domain event đang chạy trong process. Bài này
chưa kết nối Kafka và không thay đổi workflow đặt món; mục tiêu là tạo một biên public ổn định trước khi
chọn transport.

## Mục đích bài học

`OrderItemAccepted` và `OrderItemPrepared` hiện tại là domain event: chúng mô tả điều vừa xảy ra trong
model và có thể thay đổi cùng implementation của modular monolith. Message gửi qua broker có vòng đời
khác: publisher và consumer có thể được deploy ở thời điểm khác nhau, message cũ có thể được replay, và
Phase 4 sẽ tách module thành process riêng.

Vì vậy assembly `CoffeeShop.IntegrationContracts` sở hữu ba loại public:

- `IntegrationEventEnvelope<TPayload>` chứa identity, semantic type, version, thời điểm, correlation và
  causation;
- `OrderPlacedV1` chứa order ID và các line item cần được chuẩn bị;
- `OrderItemPreparedV1` chứa kết quả cần thiết để Counter hoàn thành đúng line item.

Assembly này không tham chiếu ASP.NET Core, EF Core, Kafka, Dapr, module hay cả `CoffeeShop.Contracts`.
Architecture test khóa dependency direction đó ngay từ bài đầu Phase 3.

## Contract tối thiểu

`OrderPlacedV1` chỉ công bố `OrderId`, `LineItemId`, tên item và station. Nó cố ý không chứa
`LoyaltyMemberId`, location, order source, giá hoặc payload HTTP hoàn chỉnh vì Barista/Kitchen không cần
những dữ liệu đó. Contract nhỏ hơn giảm coupling và giảm phạm vi dữ liệu bị nhân bản qua broker.

`OrderItemPreparedV1` giữ `LineItemId`, `MadeBy` và `OccurredAtUtc`. Đây không phải dữ liệu trang trí:
Counter hiện dùng chúng để hoàn thành đúng item và phát `OrderUpdated` với cùng maker/thời điểm, nên bỏ
một trong các field này sẽ làm thay đổi behavior đã có.

Item type và station đi qua wire bằng tên có nghĩa như `Latte` và `Barista`, không dùng ordinal của enum
nội bộ. Việc chèn hoặc sắp xếp lại enum trong source vì thế không âm thầm đổi ý nghĩa message.

## Version và envelope

Wire identifier là tên semantic, không phải CLR full name:

```text
coffeeshop.order-placed:1
coffeeshop.order-item-prepared:1
```

Đổi namespace hoặc tên class không tự động làm vỡ consumer. Một thay đổi tương thích như thêm field tùy
chọn chưa nhất thiết tạo Version 2; thay đổi semantic không còn đọc được bởi consumer V1 mới cần version
mới và migration plan.

Mỗi envelope có `MessageId` riêng. `CorrelationId` nối các message thuộc cùng một order workflow;
`CausationId` chỉ ra message trực tiếp tạo ra message hiện tại. Lesson 27 sẽ đưa hai identity này xuyên
suốt HTTP, Outbox, Kafka, log và SignalR; bài này mới định nghĩa contract để các bài sau không phải sửa
wire shape.

Các property bắt buộc dùng `JsonRequired`. Golden JSON fixtures khóa camel-case property name, UUID,
timestamp và payload shape. Test parse JSON theo cấu trúc thay vì phụ thuộc whitespace, đồng thời chứng
minh message thiếu field bắt buộc bị từ chối.

## Verification

Chạy test contract và architecture riêng:

```bash
dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj
dotnet test tests/CoffeeShop.ArchitectureTests/CoffeeShop.ArchitectureTests.csproj
```

Sau đó chạy toàn solution:

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx -c Release --no-restore
dotnet test CoffeeShop.slnx -c Release --no-build
```

Lesson 21 không cần Kafka container. Nếu endpoint đặt món, fulfillment, SignalR, authentication, Redis
hoặc health thay đổi thì đó là regression, không phải kết quả mong muốn của bài này.

## Summary kiến thức

- Domain event thuộc model/process; integration event là public contract giữa các boundary độc lập.
- Version phải gắn với semantic contract, không gắn với tên CLR hoặc serialization format.
- Chỉ publish dữ liệu consumer thật sự cần; contract tối thiểu vừa giảm coupling vừa giảm data exposure.
- Stable line-item identity giúp consumer idempotent ở các lesson sau.
- Correlation trả lời “cùng workflow nào”, causation trả lời “do message nào trực tiếp gây ra”.
- Golden fixture và architecture test biến compatibility/dependency direction thành quy tắc chạy được.
