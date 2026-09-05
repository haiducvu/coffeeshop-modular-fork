# Service data ownership

Kafka topology từ Lesson 33 có ba logical databases trên cùng PostgreSQL server local:

| Process | Connection setting | Database / login role | Schema và migrations |
| --- | --- | --- | --- |
| API / Counter | `ConnectionStrings:CoffeeShop` | `coffeeshop_counter` | `counter` |
| Barista Worker | `ConnectionStrings:Barista` | `coffeeshop_barista` | `barista` |
| Kitchen Worker | `ConnectionStrings:Kitchen` | `coffeeshop_kitchen` | `kitchen` |

`deploy/postgres/init-service-databases.sh` tạo role/database khi chưa tồn tại, revoke database privileges
của `PUBLIC`, rồi grant quyền cho owner tương ứng. Role được tạo với `NOSUPERUSER`, `NOCREATEDB`,
`NOCREATEROLE`, `NOREPLICATION`, `NOBYPASSRLS`. Mỗi role là database owner để chạy EF migrations trong
curriculum local. Quyền DDL vẫn rộng trong database của mình; production nên dùng migration credential
riêng như hướng triển khai Lesson 35. Đây là isolation bằng PostgreSQL authorization, không phải ba server.

Database có quyền `CONNECT` cho `PUBLIC` theo mặc định, nên chỉ đổi connection string không tạo isolation.
Testcontainers chạy **chính script deployment**, migrate bằng từng role, đọc own Outbox và kiểm tra sáu
hướng CONNECT chéo đều trả SQLSTATE `42501`. Query schema service khác trong own database trả `42P01`.
Bootstrap chạy hai lần để kiểm tính lặp lại; không tự thay password của role đã tồn tại.

Nguồn PostgreSQL 17: [privileges](https://www.postgresql.org/docs/17/ddl-priv.html),
[CREATE ROLE](https://www.postgresql.org/docs/17/sql-createrole.html),
[psql variable quoting](https://www.postgresql.org/docs/17/app-psql.html).

## Runtime và operator

API distributed chỉ register/migrate Counter. Mỗi Worker chỉ register/migrate station của mình.
Process không nhận credential của service khác. Bootstrap administrator nằm ở PostgreSQL container và
không được truyền cho ba application processes trong Kafka topology.

Smoke là operator, đọc mỗi database qua `query-service-database.sh` bằng đúng credential của owner qua TCP.
Nó tổng hợp counts và correlation JSON ở phía script; không dùng SQL join xuyên ownership boundary.
Helper giới hạn connect timeout và statement timeout. Application không gọi helper này.

Counter biết food/drink đã xong từ `OrderItemPreparedV1` qua Kafka; không đọc bảng của station.
Local transaction bảo vệ Inbox + effect + Outbox. State giữa databases hội tụ theo event delivery.

## Embedded compatibility

`CoffeeShop.Hosting.Embedded` chứa wiring Barista/Kitchen cho direct development và Dapr embedded.
API không tham chiếu trực tiếp các station runtime types; architecture test khóa boundary đó.
Assembly tương thích vẫn có mặt như transitive dependency của API image, nhưng không register trong
Kafka external mode. Đây là composition compatibility, không phải shared worker framework.

`compose.dapr.yaml` đặt API về Embedded và trỏ tới database legacy `coffeeshop` bằng credential bootstrap
local. Chỉ start `postgres redis kafka api dapr-sidecar` trong topology này. Nó không cung cấp isolation
giữa ba module trong cùng process; các service databases vẫn bị khóa đối với service roles khác.
Không dùng Dapr embedded làm bằng chứng database-per-service và không chạy workers cùng topology đó.

## Volume, secrets và chuyển checkpoint

Entrypoint PostgreSQL chỉ chạy init script tự động khi data directory trống. Volume Lesson 32 chứa shared
database sẽ không tự chuyển dữ liệu sang ba database. Giữ dữ liệu cũ bằng cách giữ volume và dùng checkpoint
Lesson 32, hoặc backup/restore có chủ đích. Lesson 33 không cung cấp zero-downtime data migration.

Fresh demo có thể xóa volume local bằng `docker compose down --volumes --remove-orphans`; thao tác này mất
dữ liệu demo và Kafka offsets, chỉ thực hiện khi chấp nhận reset. Nếu cần giữ dữ liệu, dùng Compose project
name và port overrides mới để tạo volume khác. Không copy riêng business tables mà bỏ Inbox/Outbox/offsets.

`COUNTER_DB_PASSWORD`, `BARISTA_DB_PASSWORD`, `KITCHEN_DB_PASSWORD` phải nhất quán giữa PostgreSQL bootstrap
và service environment. Defaults `*-local` chỉ dùng cho demo. Script đọc password bằng `\getenv`, quote SQL
bằng `%L`, quote identifier bằng `%I` và không echo password. Chạy lại bootstrap giữ credential đã có;
đổi env trên volume cũ không tự rotate database password. Không dùng bootstrap này để sửa cluster có grants
hoặc role memberships do operator thay đổi; audit và rotate riêng trước rollout production.
