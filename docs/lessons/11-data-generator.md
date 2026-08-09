# Bài 11: Data generator hữu hạn và deterministic

## Mục tiêu

Tạo một .NET Worker Service gửi order demo tới CoffeeShop API, nhưng vẫn chạy có giới hạn, dừng sạch khi bị cancel và cho kết quả ngẫu nhiên tái lập được bằng seed.

## Vấn đề trong source gốc

DataGen gốc thể hiện được happy path, nhưng có ba đặc điểm khiến test và automation khó tin cậy:

- Tạo `new Random()` trong từng vòng lặp nên không tái lập được chuỗi dữ liệu.
- Vòng lặp chạy cho tới khi process bị dừng, không có giới hạn số request.
- `EnsureSuccessStatusCode()` làm worker kết thúc ngay ở HTTP error đầu tiên.

Bài này giữ nguyên mục đích tạo một món barista và một món kitchen cho mỗi order, đồng thời biến các giới hạn thành configuration rõ ràng.

## Luồng xử lý

```text
OrderGeneratorOptions
        │
        ├── Seed ──► RandomOrderFactory ──► GeneratedOrder
        │
        ├── ApiBaseUrl ───────────────────► HttpClientFactory
        │
        └── OrderCount + Interval
                         │
                         ▼
              OrderGeneratorWorker
                         │ POST /v1/api/orders
                         ▼
                    CoffeeShop API
```

## Configuration có kiểu và validate sớm

Section `OrderGenerator` được bind vào `OrderGeneratorOptions`:

```json
{
  "OrderGenerator": {
    "ApiBaseUrl": "http://localhost:8080",
    "OrderCount": 10,
    "Interval": "00:00:01",
    "Seed": 20260808
  }
}
```

`ValidateOnStart()` buộc lỗi URI, số order hoặc interval xuất hiện lúc host khởi động. Đây là failure dễ chẩn đoán hơn việc đợi tới request đầu tiên mới phát hiện configuration sai.

Environment variable dùng dấu `__` để override nested key, ví dụ:

```bash
OrderGenerator__ApiBaseUrl=http://api:8080 \
OrderGenerator__OrderCount=3 \
dotnet run --project src/CoffeeShop.DataGen
```

## Deterministic randomness

`RandomOrderFactory` tạo đúng một `Random` từ seed và tái sử dụng nó. Cùng seed tạo cùng chuỗi item:

- Barista item nằm trong enum values `0..5`.
- Kitchen item nằm trong enum values `6..9`.
- `TimeProvider` tách clock thật khỏi test.

Deterministic không có nghĩa mọi order giống nhau. Nó có nghĩa chuỗi pseudo-random có thể phát lại để debug và test.

## Worker hữu hạn và cancellation

`OrderCount` là upper bound cho số HTTP calls. HTTP non-success được log rồi worker tiếp tục trong bound này; worker không retry vô hạn. Cancellation trong HTTP call hoặc delay được xem là shutdown bình thường.

Khi tạo xong số order cấu hình, worker gọi `StopApplication()` để process kết thúc. Điều này quan trọng với CLI, CI và Compose profile chạy theo batch; chỉ để `BackgroundService.ExecuteAsync()` return chưa chắc Generic Host tự dừng.

`IOrderGenerationDelay` là seam nhỏ quanh time. Production dùng `Task.Delay` với `TimeProvider`; test dùng no-op delay nên không phải sleep thật.

## Chu trình TDD

1. Test compile đỏ vì chưa có factory, options, worker và delay abstraction.
2. Factory test khóa chuỗi sinh từ cùng seed và các range item hợp lệ.
3. Worker tests khóa số request tối đa, cancellation và HTTP non-success policy.
4. Implement tối thiểu bằng `IHttpClientFactory`, options validation và injected time.
5. Chạy focused tests rồi toàn solution ở Release.

## Chạy bài học

Chỉ chạy tests của DataGen:

```bash
dotnet test tests/CoffeeShop.DataGenTests/CoffeeShop.DataGenTests.csproj
```

Chạy worker với API tại local default:

```bash
dotnet run --project src/CoffeeShop.DataGen
```

## Kiến thức cần nhớ

- Khởi tạo `Random` một lần từ seed để tái lập chuỗi dữ liệu.
- Worker dùng cho demo/automation phải có upper bound rõ ràng.
- `IHttpClientFactory` quản lý client configuration và handler lifetime.
- Options nên bind thành type và validate lúc startup.
- Cancellation là control flow bình thường của hosted service, không phải lỗi cần log stack trace.
- Clock và delay là dependency nếu behavior liên quan thời gian cần test nhanh.

## Sai lầm thường gặp

- Tạo `Random` trong vòng lặp hoặc trong từng method call.
- Dùng `while (true)` mà không có budget/deadline.
- `Task.Delay` không truyền cancellation token.
- Retry mọi lỗi mãi mãi, tạo retry storm khi API down.
- Dùng `new HttpClient()` cho từng request.
- Đọc raw configuration keys rải rác trong worker.

## Bài tập

1. Thêm option chọn số barista/kitchen items trên mỗi order và validate range.
2. Viết test hai seed khác nhau tạo ít nhất một item khác nhau trong 20 order.
3. Thêm bounded retry tối đa hai lần chỉ cho `429` và `503`.
4. Thêm correlation ID header cho mỗi generated order.

## Technical debt cố ý

- Transport DTO đang được khai báo riêng và chưa sinh từ OpenAPI contract.
- Network exception chưa có retry/backoff policy; Phase 3 sẽ xử lý reliability có chủ đích.
- Demo identity và item ranges vẫn là constants bám behavior source gốc.

Bài 12 đóng gói PostgreSQL, API, SignalR client và DataGen bằng Docker Compose để chạy toàn bộ Phase 1.
