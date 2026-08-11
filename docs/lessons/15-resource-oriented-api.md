# Bài 15: Thêm resource-oriented order API

## Mục tiêu

Giữ nguyên contract ghi đơn Phase 1 tại `/v1/api/orders`, đồng thời thêm một contract resource rõ ràng tại `/v2/orders`. V2 trả về định danh resource, trạng thái ban đầu và URL canonical thay vì `200 OK` không body.

## Hai version, một use case

```text
POST /v1/api/orders  -> 200 OK
POST /v2/orders      -> 201 Created + Location + resource body
GET  /v2/orders/{id} -> 200 OK | 404 Not Found
```

Path versioning là explicit route mapping; không thêm package API versioning cho hai route cố định. Cả hai endpoint đều gọi `ICounterModule`, nên host không chạm handler, repository, aggregate hay persistence implementation của Counter.

## Resource representation

Sau khi tạo order, `POST /v2/orders` nhận request riêng gồm `orderSource`, `location`, `loyaltyMemberId`, `baristaItems` và `kitchenItems`. Item lists là số `ItemType`, vì command metadata `commandType` và `timestamp` của contract v1 không tham gia use case Counter.

Response có dạng:

```json
{
  "orderId": "00000000-0000-0000-0000-000000000000",
  "status": "InProgress",
  "links": {
    "self": "/v2/orders/00000000-0000-0000-0000-000000000000"
  }
}
```

`TypedResults.Created(path, resource)` gửi HTTP `201`, serialize body và đặt `Location` thành cùng canonical path. `GetOrderAsync` trả `OrderDetails?`, một read record công khai do Counter sở hữu. API biến record đó thành `OrderResourceResponse`; transport shape không chảy ngược vào module. Khi không có order, GET trả `404`. Problem Details cho lỗi nghiệp vụ và validation là nội dung Lesson 16, nên lesson này giữ `400`/`404` rỗng theo scope hiện tại.

## Chu trình TDD

1. `V2OrderContractTests` được viết trước mapping: test create yêu cầu `201`, `Location`, body, GET resource; thêm tests 404 missing resource và `/v1` vẫn `200`.
2. Focused RED trả `404 NotFound` cho `POST /v2/orders`, đúng vì route chưa được map.
3. `GetOrderModuleTests` được thêm trước facade; build RED với `ICounterModule` chưa có `GetOrderAsync` và `OrderDetails` chưa tồn tại.
4. Thêm `OrderDetails`, internal `GetOrderHandler`, registration và thin adapters V2; focused API/module tests GREEN.

## Chạy bài học

```bash
dotnet restore CoffeeShop.slnx
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj --configuration Debug --filter FullyQualifiedName~V2OrderContractTests
dotnet test tests/CoffeeShop.ApplicationTests/CoffeeShop.ApplicationTests.csproj --configuration Debug --filter FullyQualifiedName~GetOrderModuleTests
dotnet build CoffeeShop.slnx --configuration Release --no-restore
dotnet test CoffeeShop.slnx --configuration Release --no-build
```

Sau full gate, chạy Compose và `./scripts/phase-1-smoke.sh` để xác nhận route `/v1`, SignalR và asynchronous fulfillment flow vẫn giữ behavior Phase 1.

## Kiến thức cần nhớ

- Evolve observable API bằng versioned route, không âm thầm đổi client cũ.
- `201 Created` cần identity của resource, representation và `Location` canonical.
- Module trả public read record; host sở hữu HTTP representation/link và adapter mapping.
- Typed results khiến các HTTP outcome của endpoint rõ trong chữ ký mà không cần API-versioning package.

Lesson 16 sẽ chuẩn hóa error body thành Problem Details mà không đổi resource semantics của V2.
