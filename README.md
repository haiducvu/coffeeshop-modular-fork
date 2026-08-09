# CoffeeShop Modular — .NET 10 Learning Curriculum

Khóa học thực hành xây dựng lại [coffeeshop-modular](https://github.com/thangchung/coffeeshop-modular) bằng .NET 10, sau đó cải tiến thành modular monolith và hệ thống event-driven sử dụng Kafka.

## Cách học

Mỗi commit `lesson(NN)` là một bài học có thể build và test độc lập. Checkout commit, đọc tài liệu tương ứng trong `docs/lessons`, chạy:

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --no-restore
dotnet test CoffeeShop.slnx --no-build
```

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

## Nhánh Git

- `original/dotnet7`: đầy đủ 15 commit của source gốc để đối chiếu.
- `learning/dotnet10-rebuild`: lịch sử khóa học tuyến tính.
- `planning/dotnet10-curriculum`: design spec và implementation plans.

## Attribution

Behavior và ý tưởng ban đầu dựa trên dự án của Thang Chung. Bản fork giữ nguyên giấy phép MIT; những thay đổi .NET 10 và tài liệu tiếng Việt phục vụ mục đích học tập.
