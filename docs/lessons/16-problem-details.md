# Bài 16: Chuẩn hóa API failures bằng Problem Details

## Mục tiêu

Các API failure của `/v2` có một representation công khai, ổn định thay vì body rỗng hoặc chi tiết exception phụ thuộc runtime. Host đăng ký `AddProblemDetails`, một `CoffeeShopExceptionHandler` và `UseExceptionHandler`; exception từ endpoint đi tới đúng một nơi để đổi thành HTTP Problem Details.

## Error taxonomy

| Exception | HTTP status | `type` | `title` |
| --- | --- | --- |
| `FluentValidation.ValidationException` | 400 | `/problems/validation` | `Validation failed.` |
| `OrderNotFoundException` | 404 | `/problems/order-not-found` | `Order not found.` |
| `OrderConcurrencyException` | 409 | `/problems/order-conflict` | `Order conflict.` |
| Mọi `Exception` khác | 500 | `/problems/internal` | `An unexpected error occurred.` |

Mỗi body có `type`, `title`, `status` và `traceId`. ASP.NET Core default Problem Details writer tự thêm top-level `traceId`, nên handler không thêm extension này lần nữa. Thêm trùng key `traceId` sẽ làm JSON writer có duplicate extension key.

Validation dùng `HttpValidationProblemDetails`; `errors` group theo property name rồi sort ordinal cả key lẫn message. Vì vậy response deterministic ngay cả khi validator thay đổi thứ tự trả failures.

Ví dụ validation response:

```json
{
  "type": "/problems/validation",
  "title": "Validation failed.",
  "status": 400,
  "errors": {
    "Items": ["An order must contain at least one item."],
    "Location": ["..."]
  },
  "traceId": "00-..."
}
```

## Safe unexpected failures

`500` chỉ nói rằng request thất bại bất ngờ. Nó không serialize exception type, message, stack trace, connection string, token hoặc payload. Handler vẫn ghi exception server-side ở error level cùng `TraceId`, để operator nối response với log mà không đưa secret ra client.

`OrderNotFoundException` và `OrderConcurrencyException` là host-owned failure signals trong `CoffeeShop.Api.Errors`. API không tham chiếu Counter `Application`, `Domain`, `Infrastructure` hoặc `Internal`; test host thay `ICounterModule` bằng fake chỉ tại DI seam để kích hoạt hai branch này. `GET /v2/orders/{id}` đổi module result `null` thành host-owned `OrderNotFoundException` trước khi handler tạo 404.

## Tương thích `/v1`

`/v1/api/orders` vẫn giữ endpoint và local `400` behavior cũ. Lesson này chỉ bỏ catches tại `/v2` để validation/concurrency failures đi tới centralized handler; route, success body và response semantics của `/v1` không đổi.

## Chu trình TDD

1. `ProblemDetailsTests` được viết trước: validation và missing resource yêu cầu `application/problem+json`, exact taxonomy, `traceId`, và sorted validation keys. Lần RED đầu trả `400`/`404` rỗng nên `Content-Type` là `null`.
2. `UnexpectedFailureTests` dùng test-host `ICounterModule` fake ném một exception có connection string, password, token và payload giả; RED cho thấy exception thoát khỏi host thay vì response 500 an toàn.
3. Tests của host-owned 404/409 exceptions RED ở compile vì exception contracts chưa tồn tại.
4. Thêm một handler, factory validation deterministic, registration middleware và thin V2 adapters. Focused five tests GREEN; toàn bộ API suite tiếp tục green.

## Chạy bài học

```bash
dotnet restore CoffeeShop.slnx
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj --configuration Debug --filter "FullyQualifiedName~ProblemDetailsTests|FullyQualifiedName~UnexpectedFailureTests"
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj --configuration Debug
dotnet build CoffeeShop.slnx --configuration Release --no-restore
dotnet test CoffeeShop.slnx --configuration Release --no-build
```

Sau full gate, chạy frontend production build, `docker compose build`, stack Compose và `./scripts/phase-1-smoke.sh` để kiểm chứng `/v1`, SignalR và fulfillment flow vẫn giữ behavior Phase 1.

## Kiến thức cần nhớ

- Problem type và title là public contract, nên tập trung trong `ProblemTypes` và test literal qua HTTP.
- Một `IExceptionHandler` giữ mapping transport ở host, không kéo API vào module internals.
- Writer mặc định sở hữu `traceId`; custom handler chỉ đưa `ProblemDetails` không có extension key này.
- `500` cần correlation trong log, không cần diagnostic detail ở response.
