# Lesson 33 — Thực thi data ownership của từng service

## Mục đích bài học

Lesson 32 tách ba process nhưng còn dùng chung database. Bài này chuyển Counter, Barista, Kitchen sang
database và credential riêng, đồng thời dùng PostgreSQL permission tests để kiểm chứng ranh giới.
Một mixed order vẫn đi qua cùng Kafka contracts và hoàn tất với behavior đã học.

Sau bài này bạn có thể:

- phân biệt schema ownership, process ownership và database authorization;
- giải thích vì sao `REVOKE ... FROM PUBLIC` cần thiết;
- migrate và đọc database bằng credential riêng của service;
- kiểm chứng sáu hướng truy cập chéo bị từ chối;
- quan sát eventual consistency qua Inbox/Outbox ở từng database;
- giữ một topology embedded tương thích mà nêu rõ giới hạn của nó.

## Vertical slice

```text
API / Counter ── coffeeshop_counter
      │ OrderPlacedV1 → Kafka orders
      ├───────────────────────────────┐
      ▼                               ▼
Barista Worker                    Kitchen Worker
coffeeshop_barista                coffeeshop_kitchen
      │ OrderItemPreparedV1            │ OrderItemPreparedV1
      └──────── Kafka preparation ────┘
                     │
                     ▼
             Counter Inbox → Fulfilled
```

Mỗi nhánh có local transaction riêng. Database Counter có thể đã nhận order trong khi station chưa
consume event. Outbox bảo đảm event còn có thể publish sau crash; Inbox bảo vệ effect khi record được
giao lại. Không có cross-database transaction và không có claim exactly-once toàn hệ thống.

## Database và credential

| Owner | Database / role | Connection key | Password environment |
| --- | --- | --- | --- |
| Counter | `coffeeshop_counter` | `ConnectionStrings:CoffeeShop` | `COUNTER_DB_PASSWORD` |
| Barista | `coffeeshop_barista` | `ConnectionStrings:Barista` | `BARISTA_DB_PASSWORD` |
| Kitchen | `coffeeshop_kitchen` | `ConnectionStrings:Kitchen` | `KITCHEN_DB_PASSWORD` |

Ba databases dùng cùng PostgreSQL server để demo nhẹ. Role không là superuser và không có quyền tạo
database/role khác. Role sở hữu database của mình nên có thể chạy migrations, tạo schema, bảng và index
cần thiết. Đây chưa phải tách migration credential khỏi runtime credential.

`deploy/postgres/init-service-databases.sh` tạo role/database chỉ khi thiếu và revoke default privileges
của `PUBLIC`. Nếu chỉ tạo role mà quên revoke CONNECT, credential của Barista vẫn có thể kết nối database
Kitchen. Quyền table có thể tiếp tục chặn SELECT, nhưng đó không phải boundary mà lesson muốn bảo đảm.

Password đi từ environment vào psql `\getenv`; SQL dùng literal/identifier quoting. Script không echo secret
và không tự rotate password đã tồn tại. Bootstrap là bước provision local, không phải migration domain.

## Composition và migration

API Kafka nhận duy nhất connection Counter và chỉ chạy `MigrateCounterModuleAsync`. Barista/Kitchen
Workers dùng connection đã có từ Lessons 31–32; Compose đổi target database và login role.
Module giữ nguyên EF migrations, vì tên schema/bảng và behavior không đổi khi di chuyển database.

Để giữ Dapr embedded, wiring station chuyển vào `CoffeeShop.Hosting.Embedded`. API bỏ direct references
tới Barista/Kitchen runtime và chỉ gọi compatibility seam khi mode `Embedded`. Architecture tests kiểm
assembly dependency; API host-composition tests tiếp tục chứng minh External không register station.
Compatibility assembly không phục vụ workers và không chia sẻ repository hay domain implementation.

## Proof có giá trị gì?

`ServiceDatabaseOwnershipTests` chạy đúng bootstrap script trong PostgreSQL Testcontainer:

1. Chạy bootstrap hai lần để bắt script không idempotent.
2. Dùng từng service role gọi migrations rồi query own Outbox.
3. Kiểm database hiện tại và flags của role.
4. Query schema foreign trong own database: PostgreSQL trả `42P01`.
5. CONNECT sang hai service databases khác: PostgreSQL trả `42501`.

Ba theory cases bao phủ sáu hướng truy cập chéo. Đây là permission denial thật, không phải assert chuỗi
connection khác nhau. Test dùng literal database names để phát hiện wiring sai.

Smoke Kafka đọc mỗi database bằng credential của service qua TCP, rồi tổng hợp counts bên ngoài SQL.
Trace identity được đọc riêng theo correlation ID, kiểm root/causation/trace ID ở phía script. Smoke yêu cầu
station migrations có Inbox, Outbox và `RejectedAtUtc` trước khi gửi order. Tests split-host cũ vẫn giữ
vai trò chứng minh extraction checkpoints; fresh Compose smoke bổ sung bằng chứng ba database hiện tại.

Full-suite verification cũng làm rõ hai dạng test interference: API test hosts chia sẻ bootstrap logger
toàn process nên cần chạy tuần tự; Kafka test phải chủ động bật ActivityListener và kiểm cùng trace ID,
span ID mới, thay vì phụ thuộc listener do test khác bật. Đây là test isolation, không đổi business flow.

## Chạy bài học

Focused tests:

```bash
dotnet test tests/CoffeeShop.IntegrationTests -c Release --filter FullyQualifiedName~ServiceDatabaseOwnershipTests
dotnet test tests/CoffeeShop.ArchitectureTests -c Release
./tests/scripts/phase-4-compose-tests.sh
./tests/scripts/phase-3-smoke-tests.sh
./tests/scripts/phase-4-kitchen-smoke-tests.sh
```

Fresh Kafka demo (lệnh down xóa dữ liệu demo và offsets; backup trước nếu cần giữ):

```bash
docker compose --profile observability --profile dapr down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka schema-registry api barista-worker kitchen-worker signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
./scripts/phase-4-kitchen-smoke.sh
```

Đọc số Outbox của một owner:

```bash
docker compose exec -T postgres sh /opt/coffeeshop/query-service-database.sh kitchen \
  -At -c 'SELECT COUNT(*) FROM kitchen.outbox_messages;'
```

Fresh Dapr embedded regression dùng database legacy riêng trong cùng server:

```bash
docker compose down --volumes --remove-orphans
docker compose -f compose.yaml -f compose.dapr.yaml --profile dapr up -d \
  postgres redis kafka api dapr-sidecar
MESSAGING_ADAPTER=Dapr ./scripts/phase-3-smoke.sh
docker compose --profile dapr down --volumes --remove-orphans
```

Volume cũ không tự chạy lại init scripts. Giữ checkpoint/volume Lesson 32 hoặc chuẩn bị backup/restore
có chủ đích nếu muốn bảo toàn dữ liệu. Chi tiết quyền, secrets, migration và giới hạn compatibility nằm ở
[service data ownership](../architecture/service-data-ownership.md).

## Summary kiến thức

- Database-per-service là quyền sở hữu dữ liệu và quyền truy cập, không chỉ đổi tên connection string.
- PostgreSQL `PUBLIC` privileges phải được tính đến khi tạo boundary.
- Process độc lập vẫn có thể dùng chung server nhưng cần database/credential khác nhau.
- Migration là trách nhiệm của owner; domain contracts không phụ thuộc physical database layout.
- Outbox/Inbox cho phép các database hội tụ qua Kafka mà không cần distributed transaction.
- Test dùng credential thật phát hiện lỗi quyền mà architecture test không thấy; hai loại proof bổ trợ nhau.
- Operator có thể tổng hợp diagnostics từ nhiều owner; application không dùng database khác như API.
- Bootstrap idempotent không đồng nghĩa tự migrate dữ liệu hay tự rotate secret trên volume cũ.

Lesson 33 kết thúc ở data ownership. Lesson 34 sẽ bổ sung finite-batch và failure-demo distributed flow.
