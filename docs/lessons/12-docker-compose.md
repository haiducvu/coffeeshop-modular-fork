# Bài 12: Compose toàn bộ CoffeeShop flow

## Mục tiêu

Đóng gói và chạy vertical slice hoàn chỉnh của Phase 1: PostgreSQL lưu order, API migrate schema và xử lý barista/kitchen workflow, browser client nhận SignalR updates, còn DataGen có thể bật theo nhu cầu.

Đây là checkpoint kết thúc Phase 1 — **dựng lại behavior gốc trên .NET 10**.

## Topology

```text
Browser
  │ http://localhost:5173
  ▼
signalr-client (nginx, non-root)
  │ hub URL được build thành http://localhost:8080/message
  ▼
api (.NET 10, non-root) ── Host=postgres ──► PostgreSQL 17
  ▲       │
  │       ├── /health/live
  │       └── /health/ready ── checks PostgreSQL
  │
datagen (.NET 10, opt-in profile `demo`)
  └── http://api:8080/v1/api/orders
```

Giữa containers, `api` và `postgres` là DNS service names của Compose. `localhost` bên trong container luôn chỉ chính container đó. Browser chạy trên host nên hub URL của frontend lại phải dùng published host port.

## Multi-stage images

Ba Dockerfile tách build-time khỏi runtime:

- API: .NET 10 SDK publish, sau đó chạy trên ASP.NET runtime Alpine.
- DataGen: .NET 10 SDK publish, sau đó chạy trên .NET runtime Alpine.
- Client: Node 22 chạy `npm ci` và Vite build, sau đó Nginx unprivileged chỉ serve static files.

Final images không chứa SDK, TypeScript compiler hay source tree. API chạy bằng user `app` (`uid=1654`); client chạy bằng user `nginx` (`uid=101`). API image chỉ thêm `curl` để Compose kiểm tra readiness từ đúng network namespace của container.

## Liveness và readiness

Hai endpoint trả lời hai câu hỏi khác nhau:

- `/health/live`: process ASP.NET có đang phản hồi không; không chạy dependency check.
- `/health/ready`: process có đủ dependency để nhận traffic không; production kiểm tra `CoffeeShopDbContext.Database.CanConnectAsync()` tới PostgreSQL.

Testing environment không đăng ký PostgreSQL check vì API tests dùng in-memory repository. Cùng endpoint vẫn được contract-test, còn Compose smoke test chứng minh readiness thật với PostgreSQL container.

PostgreSQL có `pg_isready`; API chỉ start sau khi database healthy; client và DataGen chỉ start sau khi API ready. Health ordering giảm startup race, nhưng API readiness vẫn là source of truth thay vì coi “container process đã start” là “application đã sẵn sàng”. EF Core/Npgsql execution strategy retry transient database failures tối đa năm lần, mỗi delay tối đa năm giây, nên migration startup không phụ thuộc hoàn toàn vào timing của Compose.

## Chạy Phase 1

Build và start core stack:

```bash
docker compose build
docker compose up -d postgres api signalr-client
./scripts/phase-1-smoke.sh
```

Mở client tại <http://localhost:5173>. Dọn stack và local database volume:

```bash
docker compose down --volumes
```

`--volumes` xóa dữ liệu PostgreSQL local của Compose project; bỏ flag này nếu muốn giữ dữ liệu giữa các lần chạy.

## Chạy DataGen opt-in

DataGen nằm trong profile `demo`, vì vậy `docker compose up` bình thường không tự tạo traffic. Chạy một batch hữu hạn:

```bash
docker compose --profile demo run --rm datagen
```

Override số order và seed:

```bash
DATAGEN_ORDER_COUNT=1 DATAGEN_SEED=42 \
docker compose --profile demo run --rm datagen
```

Trong Compose, `OrderGenerator__ApiBaseUrl=http://api:8080`; đây là container DNS, không phải host URL.

## Configuration local

Các default chỉ phục vụ local learning và đều override được:

```text
POSTGRES_DB=coffeeshop
POSTGRES_USER=coffeeshop
POSTGRES_PASSWORD=coffeeshop-local
POSTGRES_PORT=5432 (chỉ bind vào 127.0.0.1)
API_PORT=8080
CLIENT_PORT=5173
DATAGEN_ORDER_COUNT=3
```

Không dùng các credentials mặc định này ở môi trường thật và không commit file `.env` chứa secret.

## Smoke test có deadline

`scripts/phase-1-smoke.sh` thực hiện một proof hữu hạn:

1. Đợi `/health/ready` và static browser client tới deadline.
2. POST SignalR negotiate, kiểm tra connection response và hai CORS headers mà browser cần (`Allow-Origin`, `Allow-Credentials`).
3. POST một order có deterministic menu (một Latte và một CakePop) cùng loyalty ID riêng cho lần chạy hiện tại.
4. Poll `/v1/api/fulfillment-orders` tới khi đúng loyalty member duy nhất đó có status `Fulfilled`.
5. Thất bại với exit code khác 0 và in `docker compose ps/logs` nếu hết hạn.

Mọi HTTP call đều có connect timeout và total timeout lấy từ global deadline; không request nào có thể treo vòng lặp vô hạn. Có thể đổi `SMOKE_TIMEOUT_SECONDS`, `API_URL` và `CLIENT_URL` khi chạy ở port khác.

## Chu trình TDD và verification

1. API contract tests đỏ với `404` cho cả health endpoints.
2. Thêm liveness/readiness mapping và PostgreSQL readiness check để tests xanh.
3. Validate expanded Compose model bằng `docker compose config`.
4. Build cả ba multi-stage images, gồm DataGen profile.
5. Start PostgreSQL/API/client thật; smoke test kiểm tra static client, SignalR negotiation, rồi đặt và fulfill order.
6. Chạy DataGen container một order và xác nhận process tự exit `0`.
7. CI lặp lại Release build/test, frontend build, image build và Compose smoke.

## Kiến thức cần nhớ

- Container started không đồng nghĩa application ready.
- Liveness không nên phụ thuộc database; readiness nên phản ánh critical dependency.
- Containers gọi nhau bằng Compose service name.
- Frontend build-time URL được browser dùng, nên phải trỏ tới host-accessible address.
- Multi-stage build giảm kích thước và attack surface của final image.
- Runtime container nên chạy non-root.
- Profile giữ demo traffic khỏi default production-like stack.
- End-to-end smoke test cần deadline, diagnostic logs và exit code đáng tin cậy.

## Sai lầm thường gặp

- Dùng `localhost` từ API để kết nối PostgreSQL container.
- Chỉ dùng `depends_on` dạng list rồi giả định database đã ready.
- Đặt compiler/SDK trong runtime image.
- Chạy mọi container bằng root.
- Embed secret vào Dockerfile hoặc commit `.env`.
- Smoke test poll vô hạn hoặc thất bại mà không in logs.
- Cho DataGen chạy mặc định và tạo dữ liệu ngoài ý muốn.

## Bài tập

1. Đổi `API_PORT` và `CLIENT_PORT`, rebuild client rồi xác nhận SignalR vẫn connect.
2. Stop PostgreSQL và so sánh response của `/health/live` với `/health/ready`.
3. Chạy DataGen hai lần cùng seed và đối chiếu item sequence trong database.
4. Thêm resource limits cho services và quan sát behavior khi giới hạn memory thấp.

## Technical debt chuyển sang phase sau

- API migration hiện chạy trong application startup với bounded transient retry; production lớn nên tách migration job/strategy rõ ràng.
- Health response mới ở dạng text tối giản, chưa có observability metadata.
- SignalR không replay event nếu browser offline.
- Domain events vẫn in-process; chưa có Outbox hoặc Kafka.
- Compose là local orchestration, chưa phải production deployment manifest.

Phase 2 bắt đầu ở bài 13 và chuyển cấu trúc hiện tại thành modular monolith. Kafka được giữ cho Phase 3, sau khi module boundaries và transactional Outbox đã đủ chắc.
