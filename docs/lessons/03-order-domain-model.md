# Bài 03: Domain model và menu pricing

## Mục tiêu

Biến JSON đặt món thành một `Order` có invariant rõ ràng. Domain quyết định tên, giá và trạm chuẩn bị; client chỉ chọn loại món.

## Luồng mới

```text
PlaceOrderRequest
      │ kiểm tra enum boundary
      ▼
ItemSelection[] ──► MenuCatalog
                         │ giá + station chuẩn
                         ▼
                    Order aggregate
                         │
                         ▼
                 InMemoryOrderStore
```

## Domain vocabulary

- `Order`: aggregate root đại diện một đơn hàng.
- `LineItem`: entity thuộc Order, có ID và lifecycle riêng trong aggregate.
- `MenuItem`: dữ liệu menu bất biến gồm tên, giá và station.
- `ItemSelection`: ý định chọn món cùng station mà transport khai báo.
- `PreparationStation`: Barista cho đồ uống, Kitchen cho đồ ăn.
- `DomainException`: invariant hợp lệ về mặt kiểu dữ liệu nhưng không hợp lệ về nghiệp vụ.

## Quyết định thiết kế

`MenuCatalog` sở hữu giá. Nếu nhận giá từ request, client có thể đặt Cappuccino với giá 0. Domain cũng đối chiếu station: Croissant trong `baristaItems` bị từ chối thay vì đi sai quy trình.

Enum giữ numeric value 0–9 tương thích source gốc, nhưng domain dùng tên PascalCase để code dễ đọc. Boundary kiểm tra `Enum.IsDefined`; giá trị 999 trả 400. Source gốc fallback item lạ thành Cappuccino, một lỗi nguy hiểm đã được sửa có test bảo vệ.

Collection line item chỉ sửa được bên trong `Order`. Constructor rỗng/private được chuẩn bị cho persistence, nhưng caller phải dùng `Order.Place` để tạo state hợp lệ.

## Chu trình TDD

1. Domain tests được viết khi type chưa tồn tại; build fail đúng vì thiếu API mong muốn.
2. Model tối thiểu làm 14 domain tests xanh.
3. API test yêu cầu order thật xuất hiện trong store; compile fail vì store chưa tồn tại.
4. In-memory adapter và mapping tối thiểu làm test xanh.
5. Test unknown item nhận 500 trước fix và 400 sau fix.

## Build và test

```bash
dotnet test tests/CoffeeShop.DomainTests/CoffeeShop.DomainTests.csproj
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj
dotnet test CoffeeShop.slnx
```

## Kiến thức cần nhớ

- Aggregate bảo vệ invariant và quyền thay đổi state.
- Transport DTO không nên trở thành domain entity.
- Giá tiền phải do server/domain sở hữu.
- Exhaustive switch tốt hơn fallback âm thầm cho unknown enum.
- Test kỳ vọng dùng literal độc lập với implementation.

## Sai lầm thường gặp

- Public setter cho mọi thuộc tính aggregate.
- Tin station hoặc price từ client mà không đối chiếu.
- Serialize domain entity trực tiếp làm API contract.
- Dùng exception chung cho cả lỗi programming và lỗi domain.

## Bài tập

1. Thêm test LoyaltyMemberId rỗng rồi quyết định đó là invariant hay validation boundary.
2. Thử gửi Croissant trong `baristaItems` và quan sát 400.
3. Giải thích vì sao `LineItems` không trả về `List<LineItem>` có thể sửa tự do.

## Technical debt cố ý

- Store chỉ nằm trong memory và mất dữ liệu khi restart.
- Endpoint trực tiếp tạo Order, chưa có application handler.
- Error response chưa có Problem Details.

Bài 04 thay in-memory store bằng EF Core và PostgreSQL thật.
