# Bài 14: Biến module boundary thành architecture tests

## Mục tiêu

Lesson 13 đã tách Counter, Barista và Kitchen thành assembly riêng. Bài này thêm ArchUnitNET để một project reference sai, framework leak trong Domain hoặc API dùng implementation nội bộ làm test fail trước khi merge.

## Architecture fitness functions

`CoffeeShop.ArchitectureTests` nạp API và năm assembly boundary đúng một lần. Các rule kiểm tra bằng dependency metadata IL:

- mỗi business module độc lập với hai module còn lại;
- SharedKernel không phụ thuộc host, Contracts, business module hay framework; Contracts không phụ thuộc host hoặc business module;
- Domain không dùng framework delivery, persistence, messaging hoặc observability;
- API chỉ gọi public facade/DTO của Counter, không gọi `Application`, `Domain`, `Infrastructure` hoặc `.Internal`.

Rule dùng `Assembly.FullName` khi chọn assembly vì đây là giá trị ArchUnitNET lưu trong architecture model. Lý do (`Because`) là một phần của failure output, để người sửa có ngay quy tắc bị vi phạm.

## Chu trình TDD

1. Thêm fixture Counter tạm thời có field của type Barista public **và** temporary test nạp architecture có test assembly.
2. Chạy focused test và quan sát ArchUnitNET báo dependency với full type name của fixture cùng reason của reusable rule.
3. Assert failure message trong test để xác nhận rule có signal hữu ích, rồi xóa fixture, test tạm và using mutation.
4. Giữ các rule production, lưu mutation copy/paste trong `docs/architecture/module-rules.md` và chạy full gate.

## Chạy bài học

```bash
dotnet restore CoffeeShop.slnx
dotnet test tests/CoffeeShop.ArchitectureTests/CoffeeShop.ArchitectureTests.csproj --configuration Debug --no-restore
dotnet build CoffeeShop.slnx --configuration Release --no-restore
dotnet test CoffeeShop.slnx --configuration Release --no-build
```

ArchUnitNET khuyến nghị chạy architecture test ở Debug để phân tích IL không bị tối ưu hóa. Full gate vẫn build và chạy Release cho toàn solution để bảo toàn convention của curriculum.

## Kiến thức cần nhớ

- Project reference tạo boundary; architecture test giữ boundary đó không bị mòn theo thời gian.
- SharedKernel càng nhỏ càng ít tạo coupling; Contracts là ngôn ngữ in-process, chưa phải broker integration contract.
- Domain purity bảo vệ business rule khỏi framework detail và làm module dễ kiểm tra hơn.
- Mutation test là bằng chứng test có khả năng phát hiện một vi phạm có chủ đích.
