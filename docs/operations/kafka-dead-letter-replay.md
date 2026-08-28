# Kafka Dead-Letter Topic: inspect và replay an toàn

Runbook này áp dụng cho các topic `{original-topic}.dlt` của CoffeeShop. Mục tiêu là điều tra, sửa nguyên nhân
và replay có audit; không dùng DLT như queue để tự động bơm record lỗi quay lại production.

## Cảnh báo trước khi thao tác

- Replay vẫn là **at-least-once**. Một record có thể được deliver hoặc forward nhiều lần.
- DLT giữ original key và raw payload bytes. Payload là controlled business data nhưng vẫn có thể chứa dữ
  liệu không nên in ra terminal, ticket hoặc chat.
- Không sửa `MessageId` khi replay cùng business event. Inbox deduplicate dựa trên original message identity.
- Không replay trước khi root cause đã được sửa và consumer mới đã healthy.
- Mỗi batch replay phải có owner, ticket/change ID, topic, offset range, lý do và kết quả xác minh.
- Kafka ACL phải chặn application producer thông thường ghi trực tiếp vào `.retry.1`, `.retry.2` và `.dlt`;
  nếu không, producer đó có thể làm sai lệch metadata vận hành dù không thể đổi routing của consumer.

## 1. Inspect metadata trước, chưa in payload

Liệt kê topic và đọc header/key của một record có giới hạn:

```bash
docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh \
  --bootstrap-server localhost:19092 --list

docker compose exec -T kafka /opt/kafka/bin/kafka-console-consumer.sh \
  --bootstrap-server localhost:19092 \
  --topic coffeeshop.orders.v1.dlt \
  --from-beginning --max-messages 1 \
  --property print.key=true \
  --property print.headers=true \
  --property print.value=false
```

Ghi lại `original-topic`, `original-partition`, `original-offset`, `delivery-attempt`, `failure-kind`,
`failure-code`, `failure-at` và `message-id`. Không copy toàn bộ output nếu header ngoài allow-list chứa dữ liệu
nhạy cảm.

## 2. Classify và sửa root cause

1. Đối chiếu `failure-code` với deployment/log cùng thời điểm; log bằng message/correlation ID, không tìm bằng
   payload.
2. Permanent failure: xác nhận producer contract/version hoặc validation bug đã được sửa. Không replay record
   thực sự domain-invalid chỉ để làm DLT trống.
3. Transient failure: xác nhận database/broker/downstream đã ổn và retry mới không tạo overload lặp lại.
4. Deploy fix, chờ `/health/ready` healthy và chạy một canary hợp lệ trước replay batch.

## 3. Chuẩn bị replay record

Dùng một replay tool được review, đọc theo topic/partition/offset cụ thể và publish về `original-topic`. Tool
phải:

- copy nguyên key và raw value bytes;
- chỉ giữ allow-list `message-id`, event type/version, occurred-at, correlation, causation, content type và
  trace headers; không copy header lạ từ DLT;
- bỏ retry/DLT-only headers: `delivery-attempt`, `not-before`, `original-*`, `failure-*`;
- chờ broker ACK (`acks=all`) trước khi ghi audit success;
- giới hạn batch/rate và dừng ngay nếu DLT mới tiếp tục tăng.

Không dùng `kafka-console-producer` để replay production payload: CLI dễ làm đổi bytes/header hoặc lộ payload
trong shell history. Repo hiện chưa cung cấp replay executable tự động; đây là deliberate safety boundary của
bài học.

## 4. Xác minh sau replay

Với từng batch:

1. Xác nhận consumer lag quay về ổn định và không có spike ở `.retry.1`, `.retry.2` hoặc `.dlt`.
2. Kiểm business state/module Inbox bằng `MessageId`; duplicate no-op là kết quả hợp lệ.
3. Kiểm Outbox pending trở về 0 và workflow đích hoàn tất.
4. Ghi số record attempted, acknowledged, duplicate, succeeded và failed vào audit ticket.
5. Chỉ khi retention/audit policy cho phép mới xóa hoặc expire DLT record; không xóa để che failure chưa xử lý.

## Khi phải dừng replay

Dừng ngay nếu thấy contract mismatch mới, DLT tăng, consumer restart loop, database contention, business effect
không idempotent hoặc không đối chiếu được original `MessageId`. Giữ nguyên record và escalation cho owner của
producer contract cùng module consumer.
