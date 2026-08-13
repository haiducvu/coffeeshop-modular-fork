# Lesson 19 — Cache fulfillment read model với Redis

Read model fulfillment là đường đọc có thể được gọi liên tục bởi dashboard. Bài này thêm cache-aside
Redis mà không để Redis lan vào Domain, Contracts hay public `ICounterModule`.

## Thiết kế

`GetFulfilledOrdersHandler` hỏi `IFulfillmentOrdersCache` nội bộ trước. Cache hit trả read record đã
serialize; cache miss, dữ liệu JSON lỗi hoặc Redis timeout/lỗi đều đọc PostgreSQL như trước rồi cố gắng
populate cache. Mọi failure cache chỉ ghi warning, không làm hỏng read path PostgreSQL.

Trên cache miss, Counter dùng một gate nội bộ chung trong process: sau khi lấy gate, nó đọc cache lần nữa
rồi mới query PostgreSQL và ghi Redis. Invalidation `Fulfilled` lấy đúng gate đó trước khi `DEL`. Thứ tự
này ngăn một reader cũ ghi lại snapshot stale sau invalidation, đồng thời gộp các cache miss đồng thời.
Đây là bảo đảm cho deployment một API process của bài học; nhiều replica cần distributed fencing/CAS ở
tầng Redis, nằm ngoài phạm vi Lesson 19.

Adapter Redis dùng key `fulfilled-orders:v1`, JSON options tường minh (bắt buộc constructor parameter,
nên payload hợp lệ cú pháp nhưng thiếu field như `[{}]` cũng là cache miss) và TTL mặc định một phút. TTL cấu
hình được kiểm tra trong đoạn đóng `[5 giây, 1 giờ]`. `AddStackExchangeRedisCache` giữ một
`IDistributedCache` singleton với shared Redis multiplexer; connect, sync và async command đều bị chặn
ở một giây.

Host có thể đặt `FulfillmentCache__TimeToLive` theo cú pháp `TimeSpan` (ví dụ `00:00:30`); cấu hình
trống dùng mặc định một phút. Giá trị parse sai hoặc ngoài khoảng bị từ chối khi khởi động.

`OrderUpdated` chỉ xóa cache khi trạng thái đơn là `Fulfilled`. Repository dispatch domain event sau
khi transaction Counter đã `SaveChanges` thành công, vì vậy invalidation không đi trước persistence.
Place order không đọc hoặc ghi cache. Ba metrics không có customer/order label là
`coffeeshop.fulfillment.cache.hit`, `.miss` và `.invalidation`.

## Chạy local

```bash
docker compose up -d --build postgres redis api signalr-client
./scripts/phase-1-smoke.sh
```

Smoke đặt một order deterministic, poll fulfillment rồi xác nhận Redis chứa `fulfilled-orders:v1` trong
global deadline. Redis chỉ publish loopback (`127.0.0.1:6379`) và API chỉ bắt đầu sau Redis healthy.
Không cần Redis cho `Testing`: chỉ việc bỏ `ConnectionStrings__Redis` là Counter tiếp tục dùng query
PostgreSQL trực tiếp.

## Kiểm chứng

Integration test dùng `Testcontainers.Redis` với đúng image `redis:8-alpine`: set/get/remove thông qua
adapter và đọc TTL thực từ Redis. Unit/application test bao phủ hit, miss/populate, payload malformed,
Redis failure fallback + warning, invalidation, lệnh command không chạm cache, metrics không nhãn nhạy
cảm và hai biên TTL.
