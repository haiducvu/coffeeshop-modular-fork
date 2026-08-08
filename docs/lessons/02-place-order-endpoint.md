# Bài 02: Endpoint đặt món đầu tiên

## Mục tiêu

Tạo vertical slice HTTP nhỏ nhất cho behavior gốc: `POST /v1/api/orders` nhận đúng JSON contract và trả `200 OK`. Bài này cố ý chưa xử lý nghiệp vụ để tách bạch transport contract khỏi domain behavior.

## Kiến thức cần có trước

- Hoàn thành Bài 01.
- Biết HTTP request/response và JSON cơ bản.

## Luồng hiện tại

```text
HTTP POST
   │
   ▼
PlaceOrderRequest
   │
   ▼
200 OK
```

Test dùng `WebApplicationFactory<Program>` để khởi động API thật trong memory. `HttpClient` gửi JSON qua HTTP pipeline; vì vậy test bảo vệ route, method, JSON binding và status code mà client quan sát được.

## Các file quan trọng

- `PlaceOrderRequest.cs`: DTO tại transport boundary.
- `PlaceOrderEndpoint.cs`: route mapping mỏng.
- `PlaceOrderEndpointTests.cs`: functional test của contract.
- `client.http`: request mẫu có thể chạy thủ công.

## Chu trình RED → GREEN

Test được viết trước endpoint. Lần chạy đầu nhận:

```text
Expected: OK
Actual:   NotFound
```

Đây là failure đúng: route chưa tồn tại. Implementation tối thiểu sau đó chỉ map route và trả `Results.Ok()`. Test chạy lại thành công.

## Vì sao vẫn dùng số cho enum?

Source gốc nhận `commandType`, `orderSource`, `location` và `itemType` dưới dạng số. Phase 1 bảo toàn observable contract này để có thể so sánh. Domain ở bài sau sẽ không dùng các `int` thiếu an toàn; endpoint phải chuyển chúng sang type có nghĩa và từ chối giá trị không xác định.

## Build và test

```bash
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj
dotnet test CoffeeShop.slnx
```

## Kiến thức cần nhớ

- Minimal API endpoint nên mỏng và giao việc cho use case/domain.
- Request DTO thuộc HTTP boundary, không phải domain entity.
- Functional test kiểm tra behavior client thấy được tốt hơn test chi tiết framework nội bộ.
- TDD cần chứng minh test fail vì feature còn thiếu trước khi viết feature.

## Sai lầm thường gặp

- Dùng request DTO trực tiếp làm entity database.
- Đưa toàn bộ nghiệp vụ vào lambda route.
- Viết implementation trước rồi thêm test chỉ luôn luôn pass.
- Thay đổi contract gốc mà không có version hoặc migration.

## Bài tập

1. Dùng `client.http` gọi API khi chạy `dotnet run --project src/CoffeeShop.Api`.
2. Đổi route trong test thành một URL sai và đọc failure.
3. Giải thích vì sao bài này chưa nên kiểm tra giá tiền món.

## Technical debt cố ý

- Endpoint chấp nhận mọi giá trị số.
- Order chưa được tạo hoặc lưu.
- Response chưa có order ID.

Bài 03 sẽ thêm domain model và biến request thành một Order hợp lệ với giá do server sở hữu.
