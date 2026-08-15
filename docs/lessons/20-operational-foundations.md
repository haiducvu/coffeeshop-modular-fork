# Lesson 20 — Structured logs và operational health

Lesson cuối Phase 2 biến host thành một process có thể quan sát và điều phối an toàn. Business module
vẫn chỉ dùng `Microsoft.Extensions.Logging`; Serilog, health endpoint và dependency probe nằm ở API host.

## Structured logging

Host tạo bootstrap logger trước `WebApplicationBuilder` để lỗi cấu hình sớm vẫn là JSON. Logger cuối đọc
`Serilog` từ configuration và services, enrich từ log context, rồi request middleware phát một event cho
mỗi HTTP request. Event có timestamp, level, rendered message, `TraceId`, `RequestPath`, `StatusCode` và
elapsed time.

`SensitiveDataDestructuringPolicy` thay Authorization, token, password, credential, connection string và
complete order payload bằng `[REDACTED]`, kể cả property lồng nhau. Formatter không serialize exception
message/stack có thể chứa secret; nó giữ `ExceptionType` để phân loại sự cố. Vì vậy code ứng dụng vẫn phải
dùng message template và không nhúng dữ liệu nhạy cảm vào literal.

## Health contract

Hai endpoint có ý nghĩa khác nhau:

- `/health/live`: chỉ chứng minh process còn chạy; predicate không chạy dependency check.
- `/health/ready`: chạy PostgreSQL và chỉ thêm Redis/identity khi dependency tương ứng được bật.

PostgreSQL open connection, Redis `PING` trên đúng singleton `IConnectionMultiplexer` mà distributed cache
dùng, và identity gọi OIDC discovery bằng named `HttpClient`. Mỗi check có deadline ngắn. Readiness trả
`503` nếu một dependency enabled lỗi; liveness vẫn `200`. Response JSON chỉ chứa tên, status và duration,
không xuất description, exception hay data nội bộ.

Identity chỉ bật sau khi `Authentication` options hợp lệ. Redis chỉ bật khi có
`ConnectionStrings:Redis`; bỏ connection string thì cả cache và Redis readiness cùng biến mất. Các setting
host quan trọng được validate ngay lúc startup với tên option rõ ràng.

## Chạy checkpoint

```bash
docker compose up -d --build postgres redis api signalr-client
./scripts/phase-1-smoke.sh
./scripts/phase-2-smoke.sh
```

Operational smoke xác nhận live là process-only, ready có PostgreSQL + Redis và API logs là newline-delimited
JSON có correlation fields. Identity flow vẫn được kiểm tra riêng:

```bash
AUTHENTICATION_ENABLED=true docker compose --profile identity up -d --build postgres redis keycloak api
./scripts/phase-2-identity-smoke.sh
```

Kafka, Outbox/inbox, DLT, Avro, OpenTelemetry, Dapr và service extraction bắt đầu ở phase sau, không được
đưa vào operational foundation này.
