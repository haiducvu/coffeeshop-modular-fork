# Bài 01: Khởi tạo solution .NET 10 có thể kiểm chứng

## Mục tiêu

Tạo nền móng nhỏ nhất cho khóa học: một ASP.NET Core host, một test project, cấu hình build dùng chung và CI. Repository chưa có nghiệp vụ CoffeeShop; đổi lại, mọi bài sau đều có checkpoint đáng tin cậy.

## Kiến thức cần có trước

- Cú pháp C# và Git cơ bản.
- Khái niệm project, package và test.

## Cấu trúc

```text
CoffeeShop.slnx
├── src/CoffeeShop.Api
└── tests/CoffeeShop.UnitTests
```

`global.json` chọn .NET 10 SDK. `Directory.Build.props` bật nullable reference types, analyzers, deterministic build và warnings-as-errors. `Directory.Packages.props` tập trung version package.

## File quan trọng

- `global.json`: quy tắc chọn SDK.
- `CoffeeShop.slnx`: solution theo định dạng XML mới.
- `Directory.Build.props`: chất lượng build dùng chung.
- `Directory.Packages.props`: central package management.
- `.github/workflows/ci.yml`: restore/build/test trên CI.

## Vì sao dùng .NET 10?

.NET 10 là bản LTS hiện hành. Source gốc pin một .NET 7 preview nên không build được trên máy chỉ có SDK mới. Khóa học dùng `net10.0` ngay từ đầu nhưng giữ nguyên behavior nghiệp vụ trong Phase 1.

`global.json` pin SDK dùng để tạo khóa học; `latestFeature` cho phép feature band .NET 10 mới hơn chạy project nhưng không tự rơi sang major version khác.

## Build và test

```bash
dotnet restore CoffeeShop.slnx
dotnet build CoffeeShop.slnx --no-restore
dotnet test CoffeeShop.slnx --no-build
```

## Kiến thức cần nhớ

- Target framework là API/runtime mà project hướng tới; SDK là công cụ restore/build/test.
- `global.json` kiểm soát SDK resolution, không thay thế `TargetFramework`.
- Central package management tránh version drift giữa project.
- Warnings-as-errors ngăn technical debt tích lũy từ bài đầu.
- Commit xanh cần bằng chứng build/test mới.

## Sai lầm thường gặp

- Pin SDK preview hoặc patch không tồn tại.
- Mỗi project tự khai báo package version khác nhau.
- CI chạy lệnh khác local.
- Test rỗng chỉ để tạo màu xanh.

## Bài tập

1. Chạy `dotnet --info` và tìm SDK được chọn.
2. Tạm tạo một compiler warning và quan sát warnings-as-errors; hoàn tác sau khi thử.
3. Giải thích khác nhau giữa `restore`, `build` và `test`.

## Technical debt cố ý

- API mới chỉ trả `Hello World`.
- Chưa có HTTP contract, domain model hoặc database.

Bài 02 sẽ thêm vertical slice đầu tiên: `POST /v1/api/orders`.
