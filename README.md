# CoffeeShop Modular — .NET 10 Learning Curriculum

Khóa học thực hành xây dựng lại [coffeeshop-modular](https://github.com/thangchung/coffeeshop-modular) bằng .NET 10, sau đó cải tiến thành modular monolith và hệ thống event-driven sử dụng Kafka.

## Cách học

Mỗi commit `lesson(NN)` là một bài học có thể build và test độc lập. Checkout commit, đọc tài liệu tương ứng trong `docs/lessons`, chạy:

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --no-restore
dotnet test CoffeeShop.slnx --no-build
```

## Chạy toàn bộ Phase 1

```bash
docker compose up -d --build postgres redis api signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
```

Client chạy tại <http://localhost:5173>. DataGen là profile opt-in:

```bash
docker compose --profile demo run --rm datagen
```

JWT Bearer authentication cũng là opt-in. Profile `identity` chạy Keycloak local,
mount realm import read-only và chỉ publish Keycloak trên loopback. Identity smoke cần
Docker Compose, `curl` và `jq` trên host:

```bash
AUTHENTICATION_ENABLED=true docker compose --profile identity up -d --build postgres redis keycloak api
./scripts/phase-2-identity-smoke.sh
```

Từ Lesson 18, `/v2` chỉ được map khi authentication bật: customer tạo/đọc đơn mình,
fulfillment-reader (hoặc operator) đọc queue, operator dùng operational routes và override
ownership có kiểm soát. `/v1`, `/message`, health và DataGen tiếp tục public. Khi auth tắt,
`/v2` trả `404` fail-closed, không tạo identity giả hay bypass policy.

`lesson17-user` / `lesson17-local`, các identity Lesson 18 và bootstrap admin credentials là dữ liệu local
không bí mật để học và smoke test; không tái sử dụng trong production. Khi auth tắt,
host không tạo identity giả và toàn bộ `/v1`, `/message`, DataGen vẫn public như Phase 1.

Lesson 19 bổ sung Redis cho read model fulfillment theo cache-aside. Cache chỉ bật khi host cung cấp
`ConnectionStrings__Redis`; không có cấu hình (bao gồm môi trường `Testing`) thì Counter tiếp tục đọc
PostgreSQL trực tiếp. Compose khởi động `redis:8-alpine` trên loopback, API chờ Redis healthy và smoke
kiểm tra key `fulfilled-orders:v1` sau khi fulfillment hoàn tất. TTL mặc định là một phút và được giới
hạn từ 5 giây đến 1 giờ; có thể đặt bằng `FulfillmentCache__TimeToLive`. Cache miss và invalidation dùng
một gate chung trong một API process để stale reader không ghi đè `DEL`; nhiều API replica cần distributed
fencing/CAS riêng, ngoài phạm vi Lesson 19.

Lesson 20 hoàn tất [checkpoint Phase 2](docs/checkpoints/phase-2.md) bằng newline-delimited JSON logs và
health contract tách bạch: `/health/live` chỉ kiểm process; `/health/ready` kiểm PostgreSQL cùng Redis và
OIDC discovery khi được bật. Readiness response chỉ công bố tên/status/duration. Redis probe tái sử dụng
đúng shared multiplexer của cache, identity probe dùng named `HttpClient` có timeout ngắn. Xem
[tài liệu Lesson 20](docs/lessons/20-operational-foundations.md) để hiểu startup validation và redaction.

Lesson 21 mở Phase 3 bằng assembly `CoffeeShop.IntegrationContracts` broker-neutral. Hai Version 1 event
dùng semantic wire name, payload tối thiểu không chứa loyalty identity và golden JSON fixture để phát hiện
breaking change trước khi Kafka xuất hiện. Xem [tài liệu Lesson 21](docs/lessons/21-versioned-integration-events.md).

Dọn containers và database volume local:

```bash
docker compose down --volumes
```

## Bắt đầu Phase 2

Lesson 13 tách Counter, Barista và Kitchen thành các deep module có schema/migration riêng. Đây là phase-boundary reset của learning environment, vì vậy hãy xóa volume Phase 1 trước lần chạy đầu tiên:

```bash
docker compose down --volumes
docker compose up -d --build postgres redis api signalr-client
./scripts/phase-1-smoke.sh
```

Các route `/v1`, SignalR client và DataGen vẫn giữ behavior cũ trên database mới. Volume reset không đại diện cho data-migration strategy của production.

## Lộ trình

- Phase 1 — dựng lại behavior gốc: Lessons 01–12.
- Phase 2 — modular monolith: Lessons 13–20.
- Phase 3 — Kafka và reliable messaging: Lessons 21–30.
- Phase 4 — distributed capstone: Lessons 31–36.

### Bài hiện tại

- [Lesson 01 — Khởi tạo solution .NET 10](docs/lessons/01-bootstrap-dotnet-10.md)
- [Lesson 02 — Endpoint đặt món đầu tiên](docs/lessons/02-place-order-endpoint.md)
- [Lesson 03 — Domain model và menu pricing](docs/lessons/03-order-domain-model.md)
- [Lesson 04 — EF Core và PostgreSQL](docs/lessons/04-ef-core-postgresql.md)
- [Lesson 05 — Query fulfilled orders bằng Specification](docs/lessons/05-query-specifications.md)
- [Lesson 06 — Dispatch use case và validation pipeline](docs/lessons/06-mediatr-validation.md)
- [Lesson 07 — Domain event trong process](docs/lessons/07-domain-events.md)
- [Lesson 08 — Barista async workflow và deterministic time](docs/lessons/08-barista-preparation.md)
- [Lesson 09 — Kitchen workflow và Order completion](docs/lessons/09-kitchen-order-completion.md)
- [Lesson 10 — Typed SignalR updates và TypeScript client](docs/lessons/10-signalr-client.md)
- [Lesson 11 — Data generator hữu hạn và deterministic](docs/lessons/11-data-generator.md)
- [Lesson 12 — Docker Compose và Phase 1 smoke test](docs/lessons/12-docker-compose.md)
- [Lesson 13 — Tách business modules và schema ownership](docs/lessons/13-module-assemblies.md)
- [Lesson 14 — Architecture tests cho module boundary](docs/lessons/14-architecture-tests.md)
- [Lesson 15 — Resource-oriented order API](docs/lessons/15-resource-oriented-api.md)
- [Lesson 16 — Chuẩn hóa API failures bằng Problem Details](docs/lessons/16-problem-details.md)
- [Lesson 17 — Xác thực API client bằng JWT Bearer](docs/lessons/17-jwt-authentication.md)
- [Lesson 18 — Phân quyền thao tác bằng policy](docs/lessons/18-policy-authorization.md)
- [Lesson 19 — Cache fulfillment read model với Redis](docs/lessons/19-redis-read-model-cache.md)
- [Lesson 20 — Structured logs và operational health](docs/lessons/20-operational-foundations.md)
- [Lesson 21 — Versioned integration events](docs/lessons/21-versioned-integration-events.md)

## Nhánh Git

- `original/dotnet7`: đầy đủ 15 commit của source gốc để đối chiếu.
- `learning/dotnet10-rebuild`: lịch sử khóa học tuyến tính.
- `planning/dotnet10-curriculum`: design spec và implementation plans.

## Attribution

Behavior và ý tưởng ban đầu dựa trên dự án của Thang Chung. Bản fork giữ nguyên giấy phép MIT; những thay đổi .NET 10 và tài liệu tiếng Việt phục vụ mục đích học tập.
