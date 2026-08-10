# Bài 13: Tách CoffeeShop thành các module assembly

## Mục tiêu

Chuyển cấu trúc ba layer dùng chung của Phase 1 thành modular monolith có boundary được compiler nhìn thấy. Ứng dụng vẫn là một process ASP.NET Core và giữ nguyên `/v1`, SignalR, DataGen cùng smoke flow, nhưng Counter, Barista và Kitchen không còn truy cập implementation của nhau.

## Project topology

```text
CoffeeShop.Api
├── CoffeeShop.Modules.Counter
├── CoffeeShop.Modules.Barista
├── CoffeeShop.Modules.Kitchen
├── CoffeeShop.Contracts
└── CoffeeShop.SharedKernel

Counter  ─┬─► Contracts ─► SharedKernel
Barista  ─┤
Kitchen  ─┘
```

`CoffeeShop.Api` là composition root nên tham chiếu cả ba module. Mỗi business module chỉ tham chiếu `Contracts` và `SharedKernel`; không module nào tham chiếu module khác. Ba project theo layer cũ (`Domain`, `Application`, `Infrastructure`) được xóa thay vì giữ wrapper tương thích, vì wrapper sẽ biến cấu trúc cũ thành một public seam thứ hai.

## Deep module và public interface

Counter sở hữu trọn vertical slice: menu pricing, Order aggregate, validation, use case đặt order/query fulfilled order, repository, EF mappings và migrations. Host chỉ gọi interface nhỏ:

```csharp
public interface ICounterModule
{
    Task<PlaceOrderResult> PlaceOrderAsync(
        PlaceOrderInput input,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FulfilledOrder>> GetFulfilledOrdersAsync(
        CancellationToken cancellationToken);
}
```

`CounterModule` là facade duy nhất của use case. Handler, repository, aggregate và `CounterDbContext` là implementation `internal`. Domain/Application/Integration tests được cấp `InternalsVisibleTo` có chủ đích để tiếp tục kiểm tra invariant và persistence mà không mở rộng production API.

Production dùng `AddCounterModule(connectionString)` với EF repository. API tests và module-interface tests dùng `AddCounterModuleForTesting()` với adapter in-memory explicit; không có configuration flag nào có thể vô tình chọn adapter test trong production.

Barista và Kitchen chỉ public composition methods. Runtime work đi vào qua `IDomainEventHandler<OrderItemAccepted>` và đi ra qua `OrderItemPrepared`, nên hai station không cần biết Counter được cài đặt thế nào.

## SharedKernel và Contracts

`CoffeeShop.SharedKernel` chỉ chứa semantics có reuse thật:

- `AggregateRoot`, `DomainException`;
- `IDomainEvent`, dispatcher và typed handler;
- `IPreparationDelay`.

Project này framework-free: không phụ thuộc ASP.NET Core, EF Core, MediatR hay Npgsql. Dispatcher dựa trên `IServiceProvider` và real `Task.Delay` là framework adapters, vì vậy nằm trong API host.

`CoffeeShop.Contracts` chứa ngôn ngữ in-process giữa các module: item/station/status enums và ba event `OrderItemAccepted`, `OrderItemPrepared`, `OrderUpdated`. Giá trị enum và payload giữ nguyên Phase 1 để JSON/SignalR không đổi. Đây chưa phải Kafka integration contracts; Kafka được giữ lại cho Phase 3.

Dispatcher resolve `IDomainEventHandler<T>` theo đúng runtime event type và gọi tuần tự. Repository persist transaction của module trước, clear event đã capture, rồi mới dispatch. Transactional gap sau commit vẫn được giữ rõ ràng; Outbox sẽ xử lý ở phase sau.

## Schema ownership và migrations

Ba context dùng chung một PostgreSQL database nhưng sở hữu schema độc lập:

```text
counter.orders
counter.line_items
barista.items
kitchen.items
```

Mỗi module có initial migration và migration history riêng:

```text
counter.__EFMigrationsHistory
barista.__EFMigrationsHistory
kitchen.__EFMigrationsHistory
```

API gọi `MigrateCounterModuleAsync`, `MigrateBaristaModuleAsync` và `MigrateKitchenModuleAsync`; host không resolve context nội bộ. Counter migration giữ foreign key `line_items -> orders` và `Order.Version` là concurrency token. Readiness mở kết nối Npgsql trực tiếp thay vì bypass module boundary để lấy một context.

## Volume reset có chủ đích

Bài 13 là phase boundary và **reset dữ liệu local có chủ đích**. Schema Phase 1 dùng shared migration history cùng layout `ordering/barista/kitchen`; bài này bắt đầu ba lịch sử migration độc lập. Trước lần chạy đầu tiên ở commit này, cần xóa Compose volume:

```bash
docker compose down --volumes
docker compose up -d --build postgres api signalr-client
```

Đây là giới hạn của learning environment, không phải chiến lược migrate production. Hệ thống thật cần một data-migration plan có backup, compatibility window và rollback riêng. Từ bài 14 trở đi, migrations tiếp tục incremental trong module sở hữu.

## Chu trình TDD

1. `CounterModuleTests` RED vì `CoffeeShop.Modules.Counter` và `ICounterModule` chưa tồn tại.
2. `ModuleSchemaTests` RED vì ba module registration/migration seams chưa tồn tại.
3. Tạo SharedKernel, Contracts và Counter facade; seam test GREEN qua adapter in-memory.
4. Tách ba context, sinh ba initial migrations; schema test GREEN với PostgreSQL thật và exact owned-table list.
5. Chuyển endpoint, realtime handler và tests sang typed event/module seams; xóa MediatR notification wrapper và ba layer cũ.
6. Chạy full Release, frontend, image build và smoke trên volume mới.

## Chạy bài học

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --configuration Release --no-restore
dotnet test CoffeeShop.slnx --configuration Release --no-build

/opt/homebrew/opt/node@22/bin/node \
  /opt/homebrew/lib/node_modules/npm/bin/npm-cli.js \
  run build --prefix src/CoffeeShop.SignalRClient

docker compose down --volumes
docker compose build
docker compose up -d postgres api signalr-client
./scripts/phase-1-smoke.sh
```

## Kiến thức cần nhớ

- Module boundary cần được thể hiện bằng project reference và public interface, không chỉ bằng folder.
- Deep module giữ domain, use case và persistence cùng một ownership boundary.
- SharedKernel chỉ nên chứa semantics có bằng chứng reuse; đưa quá nhiều thứ vào đó sẽ tạo coupling mới.
- In-process contracts không đồng nghĩa integration events dành cho broker.
- Mỗi module sở hữu schema, context, migration và migration history của mình.
- Composition root được phép biết mọi module; business module không được biết nhau.
- Refactor architecture vẫn phải bảo toàn observable behavior bằng contract, integration và smoke tests.

## Technical debt cố ý

- Domain events vẫn dispatch sau commit và có transactional gap.
- API startup tự apply migrations; production lớn nên có migration job/strategy riêng.
- Lesson boundary dùng volume reset thay vì data migration production.
- Rule cấm dependency sai mới đang được thể hiện qua project graph; bài 14 sẽ biến chúng thành architecture fitness tests.

Bài 14 thêm executable architecture rules để một dependency ngược hoặc public-surface leak làm test fail ngay.
