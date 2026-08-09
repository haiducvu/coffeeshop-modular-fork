# Bài 06: Dispatch use case và validation pipeline

## Mục tiêu

Tách orchestration của hai use case đặt món và đọc đơn fulfilled khỏi Minimal API endpoint. Mọi request đi qua cùng một validation pipeline trước khi handler được phép thay đổi dữ liệu.

## Luồng mới

```text
HTTP request
     │ map transport → command/query
     ▼
   ISender
     │
     ├── ValidationBehavior ──► IValidator<TRequest>[]
     │          │ hợp lệ
     │          ▼
     ├── PlaceOrderHandler ──► IOrderRepository
     └── GetFulfilledOrdersHandler ──► IOrderRepository
                                      │
                                      ▼
                              application result/DTO
```

Endpoint chỉ còn ba trách nhiệm: nhận transport contract, map sang application request và map kết quả/lỗi sang HTTP. Application handler không tham chiếu ASP.NET Core nên có thể được gọi từ HTTP, worker hoặc test bằng cùng một behavior.

## Command, query và CQRS-style dispatch

`PlaceOrderCommand` diễn tả ý định thay đổi state và trả về `PlaceOrderResult`. `GetFulfilledOrdersQuery` diễn tả ý định đọc và trả về read model. Đây là CQRS-style separation trong cùng application/database, chưa phải hai hệ thống read/write vật lý.

MediatR cung cấp `ISender` và tìm `IRequestHandler<TRequest,TResponse>` đã đăng ký trong Application assembly. Nó giảm coupling giữa adapter HTTP với handler cụ thể, nhưng không thay thế domain model, transaction hoặc message broker.

## Validation boundary

`PlaceOrderValidator` kiểm tra dữ liệu có cấu trúc nhưng không hợp lệ trước khi persistence chạy:

- Loyalty member ID không được rỗng.
- Order source và location phải là enum đã định nghĩa.
- Order phải có ít nhất một item.
- Mọi item type phải là giá trị enum đã định nghĩa.

`ValidationBehavior<TRequest,TResponse>` lấy toàn bộ validator cho request, chạy `ValidateAsync`, gom lỗi và ném một `ValidationException` trước khi gọi handler. Nhờ vậy validation không bị lặp trong từng endpoint hoặc từng handler.

Domain vẫn giữ invariant nghiệp vụ cuối cùng, ví dụ món phải đi đúng preparation station. Validation không thay thế domain; nó bảo vệ boundary sớm hơn và tạo failure nhất quán.

## Package và license awareness

Bài học pin `MediatR 14.2.0` và `FluentValidation 12.1.1`, là các bản stable hiện hành khi bài được tạo. Registration dùng API hiện tại:

- `RegisterServicesFromAssembly(...)` để tìm handler.
- `AddOpenBehavior(...)` để thêm generic pipeline behavior.
- `AddValidatorsFromAssemblyContaining(...)` để tìm validator.

MediatR hiện hỗ trợ license key qua biến môi trường `MEDIATR_LICENSE_KEY` hoặc `LUCKYPENNY_LICENSE_KEY`. Không lưu key trong source code, appsettings hoặc Git. Theo tài liệu hiện hành, thiếu key tạo license-category log chứ không gọi license server hay tắt tính năng; khi sử dụng thực tế vẫn phải tự kiểm tra điều khoản phù hợp với tổ chức.

## Chu trình TDD

1. Application tests được viết trước và fail compile vì command/query/handler chưa tồn tại.
2. API test loyalty ID rỗng nhận `200 OK`, đồng thời phát hiện order đã lọt tới persistence.
3. Command/query handlers, validators, pipeline behavior và DI registration tối thiểu được thêm vào.
4. Handler tests chứng minh order được add/save; validator tests bảo vệ từng nhóm input lỗi.
5. API test nhận `400 Bad Request` và số order trong store không đổi, chứng minh pipeline chặn trước handler.

## Chạy bài học

```bash
dotnet test tests/CoffeeShop.ApplicationTests/CoffeeShop.ApplicationTests.csproj
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj
dotnet test CoffeeShop.slnx
```

Docker phải chạy vì full solution vẫn chứa PostgreSQL integration tests từ Lesson 4.

## Kiến thức cần nhớ

- Transport DTO và application command là hai boundary khác nhau.
- `ISender` dispatch request trong process; Kafka ở phase sau giải quyết integration messaging giữa process.
- Pipeline behavior phù hợp với cross-cutting concern quanh mọi handler.
- Validation kiểm tra input sớm; aggregate vẫn là nơi bảo vệ invariant nghiệp vụ.
- Handler orchestration nên phụ thuộc port của Application, không phụ thuộc EF Core hay HTTP.
- Package license/configuration là một phần của quyết định kiến trúc, không chỉ là chi tiết cài đặt.

## Sai lầm thường gặp

- Đưa `HttpContext`, `IResult` hoặc status code vào Application handler.
- Tin rằng dùng tên Command/Query đồng nghĩa đã có full CQRS.
- Viết validation giống nhau trong endpoint, handler và domain.
- Dùng MediatR cho Kafka/integration events rồi nhầm in-process delivery là durable messaging.
- Hard-code license key hoặc secret trong repository.
- Đăng ký pipeline sau khi quên scan validator assembly.

## Bài tập

1. Đặt breakpoint trong `ValidationBehavior` và quan sát handler không chạy với loyalty ID rỗng.
2. Thêm validator bắt món Kitchen gửi vào Barista collection; quyết định rule thuộc boundary hay domain.
3. Gọi `ISender.Send(new GetFulfilledOrdersQuery())` từ một test không liên quan HTTP.
4. Tạm bỏ `AddOpenBehavior`, chạy API test invalid loyalty và giải thích regression.

## Technical debt cố ý

- Endpoint đang catch `ValidationException` trực tiếp; Lesson 16 sẽ chuẩn hóa lỗi bằng Problem Details.
- Handler gọi save trực tiếp; domain-event dispatch và transaction boundary sẽ được bổ sung ở Lesson 7.
- Command vẫn dùng numeric enum để giữ contract gốc; API version cải tiến sẽ dùng contract rõ hơn ở Phase 2.

Bài 07 thêm domain events và dispatch chúng đúng một lần sau khi persistence thành công.
