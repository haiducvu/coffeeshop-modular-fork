# Lesson 34 — Kiểm chứng distributed flow và phục hồi worker

## Mục đích bài học

Chuyển topology ba process/ba database thành một bài thực hành có kết quả đo được: gửi một batch hữu
hạn, ngắt một worker, khôi phục nó và phát lại event gốc. Không thay đổi API, business logic, integration
contracts hoặc thêm failure endpoint vào production code.

Sau bài này bạn biết thiết kế system test cho eventual consistency, phân biệt broker delivery với
business effect, và viết fault demo không để worker bị tắt quên khi test thất bại.

## Vertical slice

```text
Counter commit order + Outbox
        ↓
stop đúng một worker → Kafka orders có consumer lag
        ↓
start lại worker → Inbox + item + Outbox tại station
        ↓
Counter nhận hai preparation events → order Fulfilled
        ↓
replay OrderPlaced gốc → hai station commit offset, không thêm business effect
```

HTTP 200 chỉ chứng minh command đã được nhận và transaction Counter hoàn tất; nó không chứng minh
Barista/Kitchen đã xong. Test phải đợi trạng thái hội tụ thay vì sleep cố định rồi đoán thành công.

## Batch hữu hạn và bằng chứng theo owner

`scripts/phase-4-smoke.sh` mặc định gửi ba mixed orders. `DATAGEN_ORDER_COUNT` giới hạn 1–20;
`DATAGEN_SEED` chọn chuỗi menu có thể lặp lại, mỗi order luôn có một drink và một food. Đây là acceptance
harness chuyên biệt, không phải chạy lại random DataGen container: nó dùng chung hai tên setting để
demo nhất quán, nhưng cố ý bảo đảm cả hai station tham gia. DataGen của Lesson 11 giữ nguyên behavior.

Mỗi lần chạy dùng một UUID ngẫu nhiên làm loyalty marker để lọc API result; mỗi request vẫn có correlation
ID do API cấp. Marker không được in ra log. Script lấy baseline độc lập từ từng database, sau đó so delta:

| Bằng chứng cho N mixed orders | Counter | Barista | Kitchen |
| --- | ---: | ---: | ---: |
| Business rows mới | N orders | N items | N items |
| Distinct business identities mới | N OrderIds | N LineItemIds | N LineItemIds |
| Completed rows mới | 2N line items | N items | N items |
| Processed Inbox mới | 2N | N | N |
| Outbox mới | N | N | N |
| Pending Outbox cuối bài | 0 | 0 | 0 |
| Rejected Outbox tăng thêm | 0 | 0 | 0 |

Fulfillment API còn phải trả đúng N order IDs riêng biệt của run, mỗi order có hai line items Fulfilled,
thuộc đúng Barista và Kitchen. Vì đo delta, có thể chạy lại trên stack đã có dữ liệu hoàn tất. Vì đây là
global delta, không chạy song song nhiều smoke, DataGen hoặc traffic khác; baseline phải hết pending Outbox.
Script là operator test có thể đọc cả ba owner, không phải application query chéo database.

## Replay đúng event, không tạo order mới

Script đọc envelope gốc theo correlation header của request đầu tiên trong Counter Outbox. Nó publish
lại cùng MessageId, OrderId và identity fields lên orders topic, dùng JSON reader tương thích với Avro
producer hiện tại. Kafka headers được dựng đúng contract; timestamp header có bảy chữ số thập phân theo
định dạng .NET `O`, kể cả PostgreSQL JSONB trả timestamp chỉ có sáu chữ số.

Gate cần cả ba bằng chứng: end offset tăng, **cả hai** station groups hết lag, và toàn bộ business delta
không thay đổi. Ngoài ra retry/DLT end offsets không được tăng do replay. Chỉ nhìn database không đổi là
chưa đủ: record có thể chưa consume, hoặc bị decode lỗi rồi chuyển DLT. Đây là at-least-once delivery cộng
Inbox idempotency, không phải exactly-once toàn hệ thống.

## Fault và giới hạn thời gian

`phase-4-fault-demo.sh` chỉ chấp nhận `barista-worker` hoặc `kitchen-worker`, ép batch một order. Nó xác
nhận Counter đã commit rồi mới stop đúng service đó, đợi consumer lag dương, start lại và chạy cùng gate
fulfillment/replay. Nếu không quan sát được backlog trong cửa sổ demo thì fail, không tuyên bố đã chứng
minh recovery. Retry/DLT khác với worker downtime: broker giữ record khi consumer vắng mặt.

Mọi Docker/HTTP/SQL command nằm trong global deadline (mặc định 240 giây). Python standard-library helper
chạy CLI trong process group riêng và kill cả descendants khi hết thời gian; chỉ kill Docker CLI cha có
thể để Compose plugin còn giữ stdout khiến shell treo. Cleanup có một recovery budget riêng (mặc định
10 giây), vẫn cố start lại đúng worker trên lỗi hoặc signal. Nếu recovery thất bại, script exit nonzero và
in lệnh phục hồi thủ công. Diagnostics chỉ in stage và service names, không dump env, payload hay logs.

## Test và chạy bài

```bash
./tests/scripts/phase-4-smoke-tests.sh
dotnet build CoffeeShop.slnx -c Release
dotnet test CoffeeShop.slnx -c Release --no-build
```

Shell tests giả lập HTTP, SQL, broker offsets và container lifecycle theo state. Có case dữ liệu cũ,
mới hoàn toàn chưa có orders topic, seed có số 0 ở đầu,
mất order, duplicate effect, pending/rejected Outbox, thiếu worker, API unavailable, CLI treo cả child,
duplicate chưa consume, replay vào DLT, restart thất bại và không có backlog. Hai worker đều có recovery
success case; lỗi phải kết thúc hữu hạn và luôn thử phục hồi nếu đã stop.

Các bước Docker thật, retry integration tests, poison DLT, regressions SignalR/Redis, identity và Dapr
nằm trong [distributed failure-demo runbook](../runbooks/distributed-failure-demo.md). CI chạy normal batch
và hai fault scenarios liên tiếp trên volume mới; các gates cũ tiếp tục chạy riêng.

## Một lỗi test được phát hiện khi đóng gate

GitHub Actions của checkpoint trước lộ race ở test cấu hình startup: API ném đúng
`OptionsValidationException` rồi `RunAsync` dispose host; `WebApplicationFactory.DeferredHost.StartAsync`
đôi khi đọc `IServiceProvider` sau thời điểm đó, nên test nhận `ObjectDisposedException` thay vì lỗi gốc.
Tái hiện bằng cách làm chậm test thread sau `Build` xác nhận lỗi ở harness, không phải validation bị mất.

Bài này chuyển sáu failure-startup tests sang chạy chính API entry point trong process riêng. Test đọc
stdout/stderr đồng thời, đợi process kết thúc trong 30 giây, kiểm exit code khác 0 và thông báo lỗi cấu
hình đúng; timeout sẽ kill process tree với cleanup budget 5 giây. Không thêm sleep, retry assertion hay
chấp nhận `ObjectDisposedException` làm kết quả đúng. Các HTTP behavior tests vẫn dùng WebApplicationFactory.
Production code và validation rules giữ nguyên. Đây là một ví dụ về chọn đúng test boundary: process
exit là contract cần đo khi ứng dụng không thể khởi động.

## Summary kiến thức

- Eventual assertion đo kết quả hội tụ trong một deadline, không yêu cầu các process hoàn thành đồng thời.
- Run marker giúp lọc đúng kết quả; owner deltas giúp không phụ thuộc database rỗng.
- Broker offsets và database effects là hai loại bằng chứng bổ trợ, không thể thay thế nhau.
- Replay giữ MessageId cũ để kiểm idempotency; POST order mới không phải duplicate-delivery test.
- Worker downtime, transient handler failure và poison message cần những proof riêng.
- Fault injection phải giới hạn target, có recovery trap và báo lỗi khi không phục hồi được.
- Test doubles kiểm lỗi của harness; Kafka/PostgreSQL thật kiểm giả định về hệ thống.
- Test startup thất bại nên quan sát lỗi/exit của ứng dụng, không nhầm race của test host với behavior thật.

Checkpoint bài này chỉ là Lesson 34. Không có triển khai Nomad hay thay đổi thuộc Lesson 35.
