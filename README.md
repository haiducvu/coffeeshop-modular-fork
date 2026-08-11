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
docker compose up -d --build postgres api signalr-client
./scripts/phase-1-smoke.sh
```

Client chạy tại <http://localhost:5173>. DataGen là profile opt-in:

```bash
docker compose --profile demo run --rm datagen
```

Dọn containers và database volume local:

```bash
docker compose down --volumes
```

## Bắt đầu Phase 2

Lesson 13 tách Counter, Barista và Kitchen thành các deep module có schema/migration riêng. Đây là phase-boundary reset của learning environment, vì vậy hãy xóa volume Phase 1 trước lần chạy đầu tiên:

```bash
docker compose down --volumes
docker compose up -d --build postgres api signalr-client
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

## Nhánh Git

- `original/dotnet7`: đầy đủ 15 commit của source gốc để đối chiếu.
- `learning/dotnet10-rebuild`: lịch sử khóa học tuyến tính.
- `planning/dotnet10-curriculum`: design spec và implementation plans.

## Attribution

Behavior và ý tưởng ban đầu dựa trên dự án của Thang Chung. Bản fork giữ nguyên giấy phép MIT; những thay đổi .NET 10 và tài liệu tiếng Việt phục vụ mục đích học tập.
