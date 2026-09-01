# Lesson 28 — Avro và Schema Registry governance

Lesson 27 đã làm identity xuyên suốt workflow, nhưng JSON schema vẫn chỉ được bảo vệ bằng convention và golden
fixture. Bài này thêm Avro contract sinh code, Confluent Schema Registry và compatibility gate mà không đưa
broker-specific type vào module hoặc thay đổi JSON canonical đang nằm trong Outbox.

## Mục đích bài học

Mục tiêu không chỉ là đổi serializer. Một rollout an toàn phải giải quyết đồng thời:

- ai sở hữu wire schema và generated type;
- consumer cũ/mới đọc format nào trong thời gian chuyển tiếp;
- schema evolution nào được phép;
- retry/DLT có làm đổi subject hay schema identity không;
- dependency mới có tham gia readiness và smoke proof không.

Kết quả của bài là consumer dual-read `application/json` và `application/avro`, producer có switch
`Json`/`Avro`, còn cấu hình Compose mặc định phát Avro.

## Boundary và canonical Outbox

Hai file `.avsc` cùng generated `ISpecificRecord` nằm trong `CoffeeShop.Messaging.Kafka`. Module chỉ tiếp tục
tham chiếu `CoffeeShop.IntegrationContracts`; generated type không đi qua messaging port hay business handler.

```text
module transaction
  └─ Outbox JSON canonical (không đổi)
       └─ Kafka adapter
            ├─ JSON writer (rollback window)
            └─ Avro mapper -> generated record -> Schema Registry framing
```

Giữ Outbox JSON là một quyết định migration có chủ đích. Nó tách persistence contract khỏi broker format và
cho phép bật/tắt writer format mà không rewrite pending row. Đổi cả Outbox storage lẫn Kafka wire format trong
một commit sẽ làm rollback khó hơn và trộn hai bài toán độc lập.

## Schema-first và generated records

`OrderPlacedV1.avsc` và `OrderItemPreparedV1.avsc` mô tả đầy đủ envelope cùng payload. Namespace Avro
`CoffeeShop.Events.V1` tạo full record names ổn định:

- `CoffeeShop.Events.V1.OrderPlacedV1`;
- `CoffeeShop.Events.V1.OrderItemPreparedV1`.

Generated C# files được tạo bởi repo-local tool `Apache.Avro.Tools` 1.12.2 và được đánh dấu `AutoGen` trong
project. Khi schema thay đổi, chạy lại generator thay vì sửa file generated bằng tay:

```bash
dotnet tool restore
dotnet tool run avrogen -s \
  src/CoffeeShop.Messaging.Kafka/Avro/OrderPlacedV1.avsc \
  src/CoffeeShop.Messaging.Kafka/Avro/Generated
dotnet tool run avrogen -s \
  src/CoffeeShop.Messaging.Kafka/Avro/OrderItemPreparedV1.avsc \
  src/CoffeeShop.Messaging.Kafka/Avro/Generated
```

`AvroContractMapper` là anti-corruption layer giữa broker-neutral record và generated record. Mapper chuyển
UUID/timestamp sang canonical string, kiểm semantic event type/version và không để dependency Avro lan vào
domain/application/module assemblies.

## Reader-first rollout

Migration format dùng thứ tự reader trước, writer sau:

1. deploy consumer hiểu cả JSON V1 và Avro V1;
2. giữ producer ở JSON và quan sát consumer mới;
3. đặt Schema Registry policy `BACKWARD`;
4. chuyển producer sang Avro bằng `ProducerFormat=Avro`;
5. giữ JSON reader trong compatibility window để đọc record cũ/retry đang tồn tại;
6. rollback writer về `Json` nếu cần, không rollback reader.

Kafka header `content-type` tách **format version** khỏi `event-version`. `application/avro` không đồng nghĩa
business event V2; cả JSON và Avro trong bài đều mang `OrderPlacedV1`/`OrderItemPreparedV1` Version 1.
Unknown hoặc duplicated content type là permanent contract failure và đi theo retry/DLT policy Lesson 26.

## Record Name Strategy

Serializer dùng `SubjectNameStrategy.Record`, vì vậy subject là full Avro record name thay vì
`{topic}-value`. Một record được copy nguyên bytes từ original topic sang `.retry.1`, `.retry.2` hoặc `.dlt`
vẫn thuộc cùng schema subject. Topic movement là transport concern, không tạo một business contract mới.

Trade-off là full record name phải unique trong registry scope. Đổi namespace/name là đổi subject, không phải
refactor C# vô hại. Integration test serialize ở orders topic, deserialize trong retry topic và xác nhận registry
không tạo topic-bound subject.

## BACKWARD compatibility và default

`BACKWARD` yêu cầu schema reader mới đọc được data do schema trước ghi. Với field mới, reader mới chỉ đọc được
record cũ nếu field có default:

```json
{ "name": "source", "type": "string", "default": "coffeeshop" }
```

Breaking fixture cố tình thêm cùng field mà không có default. Test gọi Schema Registry thật để chứng minh
compatible fixture trả `true` và breaking fixture trả `false`; đây không phải assertion tự mô phỏng rule Avro.
Default là dữ liệu migration semantics, nên phải chọn giá trị có nghĩa và review như code business contract.

## Confluent wire framing và async adapter

`AvroSerializer<T>` đăng ký/lookup schema rồi tạo payload theo Confluent framing: magic byte, schema ID bốn byte
và Avro bytes. Adapter không tự chế framing. Vì registry lookup có I/O, mapper/publisher/consumer path chuyển
sang async; cancellation tiếp tục đi từ host lifecycle tới transport boundary.

`DualFormatIntegrationEventCodec` chọn codec bằng producer option khi ghi và bằng exact content type khi đọc.
Payload được map trở lại `IntegrationEventEnvelope<T>` trước khi đối chiếu các identity header, nên business
handler không biết record đến từ JSON hay Avro.

## Readiness và cấu hình rollback

Cấu hình mặc định:

```json
{
  "SchemaRegistryUrl": "http://localhost:8081",
  "ProducerFormat": "Avro"
}
```

Compose dùng `confluentinc/cp-schema-registry:8.1.0`, kết nối listener Kafka nội bộ và đặt compatibility mặc
định `BACKWARD`. API chỉ đăng ký `schema-registry` readiness khi writer là Avro. Nếu writer rollback về JSON,
consumer vẫn dual-read nhưng registry không còn là startup dependency của publication path.

Readiness chỉ gọi `GET /subjects` với timeout bounded và không trả body/credential ra health response. Smoke
test còn yêu cầu chính xác hai Version 1 record subjects sau workflow, nhờ đó “container healthy nhưng schema
chưa bao giờ được dùng” không thể tạo một gate xanh giả.

## Verification

Chạy unit tests, registry integration proofs và smoke behavior:

```bash
dotnet test tests/CoffeeShop.MessagingTests/CoffeeShop.MessagingTests.csproj -c Release

dotnet test tests/CoffeeShop.Messaging.IntegrationTests/CoffeeShop.Messaging.IntegrationTests.csproj \
  -c Release --filter 'FullyQualifiedName~DualFormatKafkaTests|FullyQualifiedName~SchemaCompatibilityTests'

dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj \
  -c Release --filter FullyQualifiedName~HealthSemanticsTests

./tests/scripts/phase-3-smoke-tests.sh
docker compose down --volumes --remove-orphans
docker compose up -d --build postgres redis kafka schema-registry api signalr-client
./scripts/phase-3-smoke.sh
docker compose down --volumes --remove-orphans
```

## Summary kiến thức

- Schema-first nghĩa là `.avsc` là source of truth; generated files là output có thể tái tạo.
- Generated broker type phải ở sau adapter boundary, không đi vào domain/application/module contract.
- Outbox persistence format và Kafka wire format là hai quyết định khác nhau; bài này giữ canonical Outbox JSON.
- Reader-first rollout mở rộng khả năng đọc trước khi đổi writer và làm rollback an toàn hơn.
- `content-type` mô tả serialization format; `event-version` mô tả business contract version.
- `BACKWARD` bảo vệ reader mới trước data cũ; field mới cần default có business meaning.
- Record Name Strategy giữ một subject khi cùng record di chuyển qua original/retry/DLT topics.
- Đổi Avro record namespace/name là schema identity change, không chỉ là C# rename.
- Confluent serializer quản lý magic byte/schema ID framing; application không tự định nghĩa framing song song.
- Registry I/O buộc serialization path async và phải giữ cancellation/lifecycle semantics.
- JSON dual reader là compatibility window; producer switch là operational rollback lever.
- Readiness chỉ gồm dependency cần cho mode đang chạy; Avro writer làm Schema Registry trở thành critical.
- Compatibility phải được test trên registry thật với cả positive và breaking fixture.
- Smoke proof cần kiểm subject thực tế, không chỉ kiểm process/container đã start.
