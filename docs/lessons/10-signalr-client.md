# Bài 10: Typed SignalR updates và TypeScript client

## Mục tiêu

Stream trạng thái order từ server tới browser bằng contract có kiểu rõ ràng, cấu hình CORS an toàn và một Vite client xử lý đầy đủ connection lifecycle.

## Realtime flow

```text
OrderItemAccepted ─┐
                   ├─► SignalROrderUpdatePublisher
OrderUpdated ──────┘             │
                                 ▼
                    IHubContext<OrderUpdatesHub,
                                IOrderUpdatesClient>
                                 │ ReceiveOrderUpdate
                                 ▼
                         TypeScript browser client
```

`SignalROrderUpdatePublisher` là adapter ở API host. Domain/Application không tham chiếu SignalR. Accepted event được map thành InProgress update; `OrderUpdated` giữ status thật sau state transition.

## Typed contract

```text
OrderUpdateMessage
├── OrderId / LineItemId
├── ItemType
├── ItemStatus / OrderStatus
├── MadeBy
└── OccurredAt
```

`OrderUpdatesHub : Hub<IOrderUpdatesClient>` biến method gửi client thành lời gọi C# compile-time `ReceiveOrderUpdate(message)`. So với chuỗi message ghép bằng dấu `-` trong source gốc, contract này có thể version, test và serialize rõ ràng.

Typed hub chỉ bảo vệ phía server. TypeScript vẫn khai báo interface tương ứng vì chưa có code generation; contract tests/versioning ở phase sau sẽ làm boundary cứng hơn.

## CORS và credentials

SignalR JavaScript client mặc định gửi credentials trong cross-origin request. Server dùng một named policy với đúng `ClientOrigin`, cho methods/headers và credentials. Không kết hợp `AllowAnyOrigin` với `AllowCredentials`, cũng không dùng predicate cho phép mọi origin.

Development default là `http://localhost:5173`; deployment override bằng configuration/environment `ClientOrigin`.

## Connection lifecycle

Client dùng:

- `withAutomaticReconnect([0, 2000, 5000, 10000])` cho connection đã từng thành công.
- `onreconnecting`, `onreconnected`, `onclose` để phản ánh trạng thái UI.
- Retry riêng cho initial `start()`, vì automatic reconnect không giải quyết initial connection failure.
- `textContent` và DOM APIs thay vì `innerHTML` để không render business data như HTML.

Hub URL đọc từ `VITE_HUB_URL`; chỉ biến có prefix `VITE_` được expose vào browser bundle và không được chứa secret.

## Toolchain hiện hành

- `@microsoft/signalr 10.0.11`
- `Vite 8.2.1`
- `TypeScript 7.0.2`
- Node.js `>=20.19` (CI dùng Node 22)

Máy học đang có Node 20.5 nên verification dùng Node 22 tạm qua package runner. Repo không hạ Vite để chiều runtime cũ; `engines` giúp failure xảy ra sớm và rõ.

## Chu trình TDD

1. API tests fail compile vì realtime types chưa tồn tại.
2. Recording typed hub context định nghĩa expected messages cho accepted/final updates.
3. Hub contract và publisher tối thiểu làm broadcaster tests xanh.
4. Vite strict TypeScript client được build; compiler bảo vệ DOM/null/env usage.
5. CI thêm Node 22, `npm ci` và production build để frontend không trở thành artifact chưa kiểm tra.

## Chạy bài học

```bash
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj \
  --filter FullyQualifiedName~OrderUpdateBroadcastTests

npm ci --prefix src/CoffeeShop.SignalRClient
npm run build --prefix src/CoffeeShop.SignalRClient
```

Nếu Node local thấp hơn 20.19, nâng Node 20 LTS patch mới hoặc dùng Node 22.

## Kiến thức cần nhớ

- SignalR adapter thuộc host; Domain không biết transport realtime.
- Typed server hub tránh opaque string contract.
- Automatic reconnect không retry initial `start()`.
- CORS credentialed phải liệt kê origin cụ thể.
- Browser env variables là public, không phải nơi giữ secret.
- Backend và frontend build đều là green-commit gate.

## Sai lầm thường gặp

- Broadcast chuỗi nối thủ công rồi bắt client tự parse.
- `AllowAnyOrigin().AllowCredentials()` hoặc allow-all origin predicate.
- Dùng `innerHTML` với dữ liệu event.
- Không xử lý reconnecting/closed state nên UI báo connected sai.
- Cập nhật npm package nhưng không commit lockfile.
- Build backend xanh rồi bỏ qua TypeScript production build.

## Bài tập

1. Mở hai browser tabs và xác nhận cả hai nhận cùng typed update.
2. Tắt API, quan sát reconnect state, bật lại và kiểm tra recovery.
3. Đổi tên client method ở một phía để thấy contract mismatch runtime.
4. Thêm field `location` xuyên suốt event, message và UI.

## Technical debt cố ý

- Chưa có authentication/authorization cho hub.
- Chưa có replay; client offline bỏ lỡ in-memory updates.
- TypeScript contract đang viết tay, chưa sinh từ schema.
- Realtime event vẫn phụ thuộc post-save in-process dispatch, chưa có Outbox.

Bài 11 thêm DataGen hữu hạn và deterministic để tạo order demo cho toàn flow.
