# Bài 05: Query fulfilled orders bằng Specification

## Mục tiêu

Xây vertical slice đọc danh sách đơn đã hoàn tất từ HTTP tới PostgreSQL, đồng thời giữ điều kiện query ngoài adapter EF Core.

## Luồng hoàn chỉnh

```text
GET /v1/api/fulfillment-orders
              │
              ▼
GetFulfilledOrdersEndpoint
              │ FulfilledOrdersSpecification
              ▼
       IOrderRepository
        ├── EfOrderRepository ──► PostgreSQL
        └── InMemoryOrderStore ─► API tests
              │
              ▼
      FulfilledOrderDto[]
```

Đây là một vertical slice vì một bài học giao được behavior quan sát từ bên ngoài: gọi endpoint, lọc đúng order và trả cả line items.

## Specification giải quyết vấn đề gì?

`FulfilledOrdersSpecification` đóng gói hai ý định của use case:

- Chỉ lấy `OrderStatus.Fulfilled`.
- Nạp collection `LineItems` cần cho response.

Application biểu diễn ý định bằng expression tree. Infrastructure quyết định cách dịch nó sang SQL; adapter memory compile cùng expression thành delegate. Endpoint không biết `DbContext`, còn repository không chứa method riêng cho mọi màn hình.

Specification ở đây được giữ cố ý nhỏ. Nếu hệ thống chỉ có một query đơn giản, viết specification framework tổng quát với paging, sorting, projection và caching từ đầu sẽ là over-engineering.

## Read model và EF Core

`EfOrderRepository.ListAsync` dùng `AsNoTracking()` vì đây là read-only query. EF không cần lưu snapshot để phát hiện thay đổi, giảm công việc và làm rõ ý định.

`Include(order => order.LineItems)` tránh trả về aggregate thiếu dữ liệu khi DbContext đã đóng. Query cũng sắp theo ID để kết quả có thứ tự ổn định cho consumer và test.

API không serialize domain entity trực tiếp. `FulfilledOrderDto` là contract riêng, vì vậy thay đổi mapping EF hoặc encapsulation trong domain không vô tình đổi JSON public.

## Chu trình TDD

1. Integration test được viết trước và fail compile vì chưa có `FulfilledOrdersSpecification`/`ListAsync`.
2. API test được viết trước và nhận `404 Not Found` vì route chưa tồn tại.
3. Specification, hai repository adapter, DTO và endpoint tối thiểu được thêm vào.
4. API test xanh với empty result; PostgreSQL test xanh khi chỉ trả order fulfilled và nạp line items.

## Chạy bài học

Docker phải chạy cho PostgreSQL integration test.

```bash
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj
dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj
dotnet test CoffeeShop.slnx
```

## Kiến thức cần nhớ

- Specification là query intent, không phải nơi thực thi I/O.
- Application sở hữu abstraction; Infrastructure dịch expression sang query của EF Core.
- `AsNoTracking` phù hợp với read model không sửa entity.
- Eager loading phải xuất phát từ dữ liệu use case thực sự cần.
- DTO bảo vệ API contract khỏi chi tiết domain và persistence.
- Vertical slice nên kết thúc bằng behavior chạy được xuyên qua các layer.

## Sai lầm thường gặp

- Trả thẳng `IQueryable` ra khỏi Infrastructure, làm EF Core rò qua các layer.
- Serialize entity khiến navigation/property nội bộ trở thành API contract ngoài ý muốn.
- Quên `Include`, rồi phụ thuộc ngầm vào lazy loading hoặc trả collection rỗng.
- Tracking mọi read query dù không bao giờ gọi `SaveChanges`.
- Tạo specification engine lớn hơn nhu cầu hiện tại.

## Bài tập

1. Bật SQL logging và tìm mệnh đề lọc status cùng JOIN line items.
2. Bỏ `Include`, chạy lại integration test và giải thích failure.
3. Thêm specification lọc theo location nhưng chưa sửa interface repository.
4. So sánh query entity rồi map DTO với projection DTO trực tiếp trong SQL.

## Technical debt cố ý

- Domain chưa có hành vi chuyển Order sang Fulfilled; integration test seed state qua EF metadata.
- Query chưa paging vì volume hiện tại chưa chứng minh nhu cầu.
- Mapping DTO đang ở endpoint; lesson sau sẽ giới thiệu application command/query handler khi có đủ áp lực thiết kế.

Bài 06 tách use case khỏi Minimal API endpoint bằng application handler.
