# Bài 08: Xử lý Barista bất đồng bộ và deterministic time

## Mục tiêu

Biến `OrderItemAccepted` dành cho Barista thành một workflow có delay, timestamp, persistence và event hoàn thành mà test không phải chờ giây thật.

## Vertical slice

```text
OrderItemAccepted (Station = Barista)
              │ MediatR notification
              ▼
HandleBaristaOrderItemAccepted
       ├── TimeProvider.GetUtcNow()
       ├── BaristaPreparationPolicy
       ├── IPreparationDelay
       └── IBaristaItemRepository
                    │ save PostgreSQL trước
                    ▼
          OrderItemPrepared (MadeBy = barista)
```

Handler bỏ qua Kitchen item ngay tại boundary. Với Barista item, nó ghi `TimeIn`, chờ duration theo menu, ghi `TimeUp`, persist entity rồi mới dispatch `OrderItemPrepared`.

## Giữ nguyên timing gốc

| Item | Delay |
|---|---:|
| Coffee Black / Coffee With Room | 5 giây |
| Espresso / Espresso Double | 7 giây |
| Cappuccino | 10 giây |
| Đồ uống còn lại | 3 giây |

Policy là pure function nên không chứa I/O hoặc clock. Unknown/default behavior vẫn là 3 giây như source gốc, trong khi validation ở boundary ngăn enum không hợp lệ đi vào workflow bình thường.

## Time và delay là hai dependency khác nhau

`TimeProvider` trả lời “bây giờ là lúc nào?”. `IPreparationDelay` thực hiện “chờ bao lâu?”. Tách hai vai trò giúp test điều khiển cả duration lẫn timestamp.

Production đăng ký `TimeProvider.System` và `TaskPreparationDelay`. Test dùng mutable provider cùng fake delay: khi handler yêu cầu chờ 10 giây, fake ghi lại duration, advance clock 10 giây và hoàn tất ngay lập tức.

Không gọi `DateTime.UtcNow` trong Domain/Application. `DateTimeOffset` giữ rõ offset và tránh timestamp phụ thuộc local timezone.

## Persistence và event tiếp theo

`BaristaItem` là aggregate nhỏ gồm Order/LineItem identity, menu data, `TimeIn` và `TimeUp`. EF mapping lưu nó trong `barista.items`; migration `AddBaristaItems` version-control schema.

`Complete` raise `OrderItemPrepared`, nhưng repository chỉ dispatch sau `SaveChangesAsync`. Cùng post-save rule từ Lesson 7 được giữ: consumer không nhìn thấy một Barista item chưa commit.

## Chu trình TDD

1. Theory tests được viết trước và fail compile vì Barista module/ports chưa tồn tại.
2. Mutable `TimeProvider` và advancing fake delay định nghĩa API mong muốn mà không sleep thật.
3. Handler/policy tối thiểu làm sáu timing cases cùng Kitchen-ignore case xanh.
4. EF mapping và migration thêm persistence mà không để Application phụ thuộc EF Core.
5. Full suite chạy PostgreSQL migration thật để bảo vệ schema.

## Chạy bài học

```bash
dotnet test tests/CoffeeShop.ApplicationTests/CoffeeShop.ApplicationTests.csproj \
  --filter FullyQualifiedName~BaristaPreparationTests
dotnet test CoffeeShop.slnx
```

Docker phải chạy cho full integration suite.

## Kiến thức cần nhớ

- Async workflow phải honor `CancellationToken` ở delay và persistence.
- Inject clock thay vì đọc system time trực tiếp.
- Fake delay nên xác nhận duration và hoàn tất tức thời.
- Event mới được raise tại state transition `Complete`.
- Persist-before-dispatch vẫn có dual-write gap cho tới khi có Outbox.
- Module boundary được giữ bằng port Application và adapter Infrastructure.

## Sai lầm thường gặp

- `Task.Delay` trực tiếp trong handler làm unit test mất hàng chục giây.
- Fake clock nhưng vẫn dùng delay thật, hoặc fake delay nhưng vẫn đọc clock thật.
- Dùng local `DateTime.Now` cho timestamp liên hệ giữa service.
- Dispatch prepared event trước khi Barista item được lưu.
- Cho Infrastructure model hoặc EF attribute rò vào handler.

## Bài tập

1. Thêm test cancellation và chứng minh repository không được gọi.
2. Đổi Cappuccino xuống 9 giây để thấy theory test bắt regression.
3. Query bảng `barista.items` và so sánh `TimeUp - TimeIn`.
4. Thử dùng `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider` thay custom fake.

## Technical debt cố ý

- Workflow vẫn chạy trong HTTP process và request có thể chờ duration thật.
- Chưa có retry/idempotency nếu handler nhận event trùng.
- `OrderItemPrepared` chưa cập nhật Order; Lesson 9 thêm Kitchen và completion state machine.

Bài 09 hoàn tất cả Kitchen items và chuyển Order sang Fulfilled khi mọi line item đã xong.
