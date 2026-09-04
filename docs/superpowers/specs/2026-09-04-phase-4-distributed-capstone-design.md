# Phase 4 Distributed Capstone Design

## 1. Mục tiêu

Phase 4 biến các seam đã được khóa ở Phase 3 thành một hệ thống triển khai phân tán có thể kiểm chứng.
Barista và Kitchen được tách khỏi API theo từng lesson, mỗi process tiếp tục dùng cùng integration
contracts, Transactional Outbox, Inbox idempotency, retry/DLT và trace identity đã có. Phase này kết
thúc bằng topology Docker Compose hoàn chỉnh, Nomad deployment và audit toàn bộ 36 lesson commits.

Thiết kế này triển khai Lessons 31–36 trong master curriculum mà không thay đổi thứ tự, commit subject
hoặc mục tiêu kiến thức. Kafka tiếp tục là reference transport cho distributed capstone. Dapr không bị
loại bỏ, nhưng Dapr path vẫn là alternate embedded deployment trong Phase 4; việc tạo sidecar riêng cho
mỗi extracted service nằm ngoài curriculum này.

## 2. Ràng buộc kế thừa

- Target `net10.0` và giữ package versions trong `Directory.Packages.props`.
- Giữ nguyên observable behavior của `/v1`, `/v2`, SignalR, authorization, Redis cache và DataGen.
- Domain/application code không phụ thuộc ASP.NET Core, EF Core, Kafka, Dapr, Redis hay telemetry SDK.
- Mỗi lesson là một commit build được, test xanh, có tài liệu tiếng Việt và commit message tiếng Anh.
- Push từng lesson ngay sau khi verification gate xanh; không rewrite lịch sử đã public.
- Dùng Kafka integration events giữa các process; MediatR/domain events chỉ xử lý effect trong cùng process.
- Semantics vẫn là at-least-once với Inbox idempotency; không claim distributed transaction hoặc exactly-once.
- Migrations và data access thuộc process sở hữu; Kafka/Dapr adapters không truy cập module database.
- Không log payload, loyalty identity, token, credential hay connection string.
- Lesson 31 chỉ tách Barista; không triển khai trước phạm vi Lesson 32.

## 3. Các phương án đã cân nhắc

### 3.1 Big-bang extraction

Tách Barista, Kitchen, ba physical databases và mọi Dapr sidecar trong một commit. Boundary cuối cùng sẽ
rõ nhưng lesson quá rộng, khó xác định lỗi thuộc hosting, messaging hay ownership, đồng thời làm rỗng mục
tiêu của Lessons 32–33. Không chọn.

### 3.2 Shadow worker

Chạy worker mới trong khi API vẫn đăng ký cùng Barista consumer. Kafka consumer group có thể phân phối
partition cho một trong hai process, nhưng API vẫn có khả năng thực thi Barista effect; dùng group khác sẽ
tạo duplicate business effect. Topology này không chứng minh extraction thật. Không chọn.

### 3.3 Strangler extraction với explicit hosting mode — được chọn

Mỗi module có mode `Embedded` hoặc `External`. Khi Barista là `External`, API không đăng ký Barista
DbContext, migration, consumer hay Outbox worker; process `CoffeeShop.Barista.Worker` trở thành owner duy
nhất. Kafka Compose dùng mode `External`, còn direct-development và Dapr regression giữ `Embedded` trong
Lesson 31. Lesson 32 áp dụng cùng pattern cho Kitchen, không chia sẻ implementation nội bộ giữa modules.

Cách này tạo một vertical slice triển khai độc lập nhưng vẫn giữ mỗi commit rollback được và giữ Dapr
adapter đã học ở Lesson 30.

## 4. Topology mục tiêu

Sau Lesson 32, Kafka topology là:

```text
HTTP / SignalR
      |
      v
CoffeeShop.Api (Counter)
      |  Counter Outbox -> coffeeshop.orders.v1
      +-----------------------------------------------+
                                                      |
                         +----------------------------+------------------+
                         |                                               |
                         v                                               v
             CoffeeShop.Barista.Worker                    CoffeeShop.Kitchen.Worker
             Barista Inbox/items/Outbox                    Kitchen Inbox/items/Outbox
                         |                                               |
                         +----------> coffeeshop.preparation.v1 <--------+
                                                      |
                                                      v
                                        Counter consumer in API
```

Project dependencies:

```text
CoffeeShop.Barista.Worker
├── CoffeeShop.Modules.Barista
├── CoffeeShop.Messaging.Kafka
├── CoffeeShop.Messaging.Abstractions
└── CoffeeShop.IntegrationContracts

CoffeeShop.Kitchen.Worker
├── CoffeeShop.Modules.Kitchen
├── CoffeeShop.Messaging.Kafka
├── CoffeeShop.Messaging.Abstractions
└── CoffeeShop.IntegrationContracts
```

Worker projects không tham chiếu `CoffeeShop.Api`, module còn lại hoặc ASP.NET business endpoints. API
giữ Counter composition, HTTP, auth, Redis và SignalR. IntegrationContracts vẫn là public language duy
nhất giữa processes.

## 5. Hosting mode và cutover

API nhận configuration enum cho từng extracted module:

```text
Modules:Barista:Hosting = Embedded | External
Modules:Kitchen:Hosting = Embedded | External
```

Giá trị thiếu mặc định là `Embedded` để chạy source trực tiếp và giữ compatibility với các lesson cũ.
Giá trị không xác định phải fail startup với message không chứa secret. Compose Kafka đặt Barista thành
`External` ở Lesson 31 và Kitchen thành `External` ở Lesson 32.

Rule quan trọng: một module ở mode `External` làm API bỏ toàn bộ module runtime registration, migration,
Kafka consumer và Outbox worker của module đó. Worker tương ứng luôn fail startup nếu connection string,
Kafka bootstrap server hoặc required topic settings thiếu. Không có trạng thái released nào chạy cả
embedded handler và external worker cho cùng role.

Dapr profile đặt cả hai module về `Embedded`. Đây là explicit deployment choice, không phải fallback âm
thầm. Kafka vẫn là default distributed topology và là path được Nomad triển khai.

## 6. Lesson 31 — Barista Worker

### Host

`CoffeeShop.Barista.Worker` dùng .NET Generic Host (`Host.CreateApplicationBuilder`). Host đăng ký:

- validated Barista worker options và service-specific connection string;
- `TimeProvider`, production preparation delay và message identity accessor;
- Barista module với Inbox/Outbox worker;
- Kafka adapter và duy nhất consumer role `barista` cho `OrderPlacedV1`;
- console structured logging và optional OpenTelemetry export với service name riêng.

Trước `RunAsync`, host tạo scope và gọi `MigrateBaristaModuleAsync`. Migration failure làm process fail
trước khi nhận Kafka partition. EF Core runtime migration phù hợp cho local curriculum; production
deployment ở Lesson 35 sẽ có migration task riêng vì coordinated rollout và least privilege tốt hơn.

Worker dùng cooperative cancellation từ Generic Host. Kafka consumer đóng consumer trong `finally`; Outbox
worker kết thúc qua cùng stopping token. Không dùng fire-and-forget task.

### Persistence ownership

Từ Lesson 31, chỉ Barista Worker truy cập Barista DbContext trong Kafka Compose. Worker dùng setting
`ConnectionStrings:Barista`, sở hữu Barista migrations và schema `barista`. Để lesson tập trung vào process
extraction, setting này ban đầu có thể trỏ tới cùng PostgreSQL server/database đang chứa schemas khác;
không process nào khác được runtime-register Barista persistence khi mode là `External`.

Lesson 33 chuyển các service sang logical database/credential riêng và thêm enforcement. Vì vậy “owned” ở
Lesson 31 là ownership của code path, schema và migration lifecycle; physical isolation là mục tiêu riêng
của Lesson 33.

### Container và Compose

Worker có multi-stage image trên .NET 10 runtime, chạy non-root và chỉ copy project graph cần thiết. Compose
thêm service `barista-worker`, phụ thuộc PostgreSQL và Kafka healthy, dùng cùng topic/schema configuration
với API và có restart policy hữu hạn cho local demo. API service đặt Barista hosting mode `External` và
không phụ thuộc trực tiếp vào worker readiness; end-to-end smoke chứng minh eventual completion.

### Proof

Integration test tạo hai host độc lập trong cùng test process:

1. Counter/Kitchen host ghi `OrderPlacedV1` và xử lý preparation result.
2. Barista host đăng ký Barista module và `barista` Kafka consumer.
3. Test đặt mixed order, chờ fulfillment và kiểm tra đúng một Barista item, một Barista Inbox record, một
   published Barista Outbox record và một Counter Inbox effect.

Host-composition test chứng minh API mode `External` không đăng ký Barista runtime services, còn worker có
đúng một consumer role. Compose smoke kiểm tra container worker đang chạy và workflow thật hoàn tất. Test
duplicate delivery tiếp tục dùng Inbox tests hiện có; Lesson 31 không thay đổi idempotency algorithm.

## 7. Lesson 32 — Kitchen Worker

Lesson 32 tạo `CoffeeShop.Kitchen.Worker` theo cùng public pattern nhưng compose code trực tiếp từ Kitchen
module. Không tạo generic “station worker framework” và không chia sẻ repository, migrations, options hay
domain types giữa Barista/Kitchen. API external mode bỏ Kitchen registration và worker đăng ký duy nhất role
`kitchen` cho `OrderPlacedV1`.

Contract/integration proof dùng food line item, kiểm tra Kitchen Inbox/effect/Outbox và duplicate delivery.
Barista Worker từ Lesson 31 vẫn chạy, nên mỗi released commit tiếp tục hoàn thành mixed order.

## 8. Lesson 33 — Physical data ownership

Compose tạo ba logical databases và service credentials:

- `coffeeshop_counter` cho API/Counter;
- `coffeeshop_barista` cho Barista Worker;
- `coffeeshop_kitchen` cho Kitchen Worker.

Mỗi process chỉ nhận connection string của mình và chỉ chạy migrations của module mình. PostgreSQL grants
ngăn credential của service đọc database khác. Integration tests kết nối bằng từng credential để chứng
minh own access thành công và cross-service access bị từ chối. API project bỏ compile-time references còn
sót tới Barista/Kitchen runtime composition khi Kafka distributed topology trở thành canonical.

Không dùng cross-database join hoặc distributed transaction. Counter biết trạng thái preparation duy nhất
qua `OrderItemPreparedV1`.

## 9. Lesson 34 — Distributed system proof

Docker Compose trở thành canonical three-process topology. Phase 4 smoke chạy finite batch với deterministic
seed, correlation IDs riêng và eventual polling có global deadline. Assertions bao gồm:

- mọi order hoàn tất, không mất line item;
- Inbox giữ effect count bằng unique message count;
- Outbox không còn pending/rejected record ngoài expectation;
- Kafka retry/DLT hoạt động khi inject poison/transient failure;
- dừng/restart một worker không mất order và không tạo duplicate business effect;
- SignalR/Redis read model vẫn cập nhật từ Counter.

Fault injection chỉ dùng test configuration hoặc container lifecycle; không thêm production backdoor.

## 10. Lesson 35 — Nomad deployment

Nomad jobs triển khai API, Barista Worker, Kitchen Worker và dependencies/configuration cần thiết. Job specs
dùng variables cho image tags, endpoints và resource limits; secret values chỉ được tham chiếu qua runtime
environment/Vault-compatible mechanism, không commit giá trị thật. Health checks, rolling update,
reschedule và rollback guide phản ánh đúng liveness/readiness của từng process.

CI chạy `nomad job validate` khi CLI có sẵn; nếu local không có CLI, repository vẫn có script render/static
validation deterministic và tài liệu nêu rõ giới hạn bằng chứng.

## 11. Lesson 36 — Curriculum audit và publication

Lesson cuối bổ sung lesson index, C4 diagrams, ADR summary, contributor guide và history-audit script.
Audit kiểm tra đúng 36 lesson subjects theo thứ tự, mỗi lesson có một tài liệu tiếng Việt, từng checkpoint
tag trỏ đúng commit, clean clone restore/build/test được và không có secret. Operational fix commits được
liệt kê minh bạch; không rewrite lịch sử học đã public.

## 12. Error handling và operational semantics

- Invalid hosting mode/configuration: fail trước khi host bắt đầu.
- Migration/database unavailable: worker fail startup; Compose/Nomad restart policy xử lý retry lifecycle.
- Kafka unavailable: readiness/end-to-end proof không xanh; consumer không commit offset.
- Handler transient/permanent failure: giữ nguyên retry topics/DLT contract của Phase 3.
- Crash sau DB commit trước offset commit: redelivery trở thành Inbox no-op.
- Crash sau broker ACK trước Outbox sent mark: có thể republish; downstream Inbox bảo vệ effect.
- Worker shutdown: ngừng nhận work mới, truyền cancellation và đóng consumer sạch.

API không gọi HTTP đồng bộ sang workers. Worker không gọi ngược API. Broker và contracts là integration
boundary; database không phải integration API.

## 13. Observability

Mỗi process có `service.name` riêng: `CoffeeShop.Api`, `CoffeeShop.Barista.Worker`,
`CoffeeShop.Kitchen.Worker`. W3C trace context từ Outbox được Kafka adapter inject/extract như Phase 3, nên
Jaeger hiển thị các spans cùng trace dù nằm ở process khác. Metrics giữ low-cardinality dimensions; service
name phân biệt nguồn thay vì thêm OrderId/CorrelationId thành labels.

Logs giữ message, correlation, causation, event type/version, consumer role và safe error code. Worker logs
không ghi message payload hoặc connection settings.

## 14. Verification strategy

Mỗi lesson chạy Shared Green Gate:

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --configuration Release --no-restore
dotnet test CoffeeShop.slnx --configuration Release --no-build
docker compose --profile demo --profile identity --profile observability --profile dapr config --quiet
docker compose --profile demo --profile identity --profile observability --profile dapr build
```

Lesson-specific gates thêm focused tests trước full suite, fresh Kafka Compose smoke, Dapr embedded regression,
identity, DataGen và observability proof. TDD áp dụng theo từng behavior: test phải fail vì production behavior
chưa có, sau đó minimal implementation làm test xanh.

Trước mỗi commit: `git diff --check`, kiểm tra staged diff và chạy lại gate liên quan. Sau push: local HEAD
phải bằng remote branch hash. Lesson 31 dừng sau commit/push của chính nó; không tạo file hoặc code của
Lesson 32.

## 15. Quyết định và non-goals

- Chọn staged strangler extraction, không big-bang.
- Kafka là distributed capstone transport; Dapr embedded path vẫn được regression-test.
- Không tạo synchronous service-to-service HTTP API.
- Không tạo shared station database abstraction hoặc generic worker framework.
- Không đổi integration contract V1 nếu extraction không yêu cầu semantic mới.
- Không triển khai Kubernetes, service mesh, saga orchestrator hoặc exactly-once.
- Không giải quyết production zero-downtime data migration; Lesson 35 chỉ mô tả rollout/rollback an toàn.
- Không làm Lesson 32 trong commit Lesson 31.
