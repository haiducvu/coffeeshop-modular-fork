# Bài 04: Persist Order bằng EF Core và PostgreSQL

## Mục tiêu

Thay persistence mặc định bằng PostgreSQL thật, nhưng giữ Application phụ thuộc một port nhỏ thay vì phụ thuộc EF Core.

## Kiến trúc

```text
API → IOrderRepository ← EfOrderRepository → CoffeeShopDbContext → PostgreSQL
                    ↖ InMemoryOrderStore (functional tests)
```

## Thành phần chính

- `IOrderRepository`: port do Application sở hữu.
- `EfOrderRepository`: adapter EF Core.
- `CoffeeShopDbContext`: unit of work và model entry point.
- Entity configurations: mapping domain sang schema `ordering`.
- `InitialOrdering`: migration có thể review và version-control.
- `PostgreSqlFixture`: PostgreSQL container dùng chung cho integration tests.

## Quyết định quan trọng

EF Core 10.0.10 và Npgsql provider 10.0.3 cùng major 10. Central transitive pinning khóa EF relational packages ở 10.0.10; build gate đã bắt được conflict 10.0.4/10.0.10 trước khi commit.

Domain không thêm attribute EF. Mapping nằm trong Infrastructure, enum lưu thành chuỗi ổn định và money có precision `(10,2)`. Order ID do domain tạo nên `ValueGeneratedNever`.

Functional tests dùng adapter memory thật để nhanh và cô lập HTTP behavior. Integration test dùng PostgreSQL thật qua Testcontainers để kiểm tra mapping/migration mà EF InMemory provider không thể chứng minh.

`DOCKER_API_VERSION=1.41` trong test settings giữ tương thích với Docker daemon cũ trên máy học; daemon mới vẫn hỗ trợ API này.

## TDD và bằng chứng

1. Persistence test được viết trước `CoffeeShopDbContext` và fail compile.
2. Sau mapping, build phát hiện package conflict và buộc dependency graph được căn chỉnh.
3. Testcontainers ban đầu báo client API quá mới; compatibility setting cho phép chạy test thật.
4. Migration được áp dụng vào container và Order cùng hai LineItem reload thành công.

## Chạy

```bash
dotnet tool restore
dotnet test tests/CoffeeShop.IntegrationTests/CoffeeShop.IntegrationTests.csproj
dotnet test CoffeeShop.slnx
```

Docker phải chạy để integration tests thực thi.

## Kiến thức cần nhớ

- Interface persistence thuộc Application; EF implementation thuộc Infrastructure.
- Migration là source code của schema, không dùng `EnsureCreated` thay thế trong production flow.
- Integration test cần database thật khi behavior phụ thuộc provider.
- Package cùng major chưa đủ; warnings-as-errors giúp phát hiện patch graph không nhất quán.
- `DbContext` là scoped unit of work, không phải singleton.

## Bài tập

1. Mở migration và tìm foreign key từ `line_items` tới `orders`.
2. Đổi precision price rồi tạo migration thử; xóa migration thử sau khi quan sát diff.
3. Tắt Docker và đọc failure để phân biệt lỗi môi trường với assertion failure.

## Technical debt cố ý

- Endpoint vẫn tự tạo Order, chưa có command handler.
- Query fulfilled chưa tồn tại.
- Migration chạy ngay khi API startup; production hardening sẽ tách trách nhiệm này.

Bài 05 thêm query fulfilled và specification nhỏ đúng nhu cầu.
