# Quy tắc dependency giữa các module

Các test trong `CoffeeShop.ArchitectureTests` biến boundary của modular monolith thành architecture fitness functions. Chúng phân tích metadata IL bằng ArchUnitNET, không tìm chuỗi trong source code.

## Quy tắc được kiểm tra

- Counter, Barista và Kitchen không được phụ thuộc lẫn nhau.
- SharedKernel không được phụ thuộc host, Contracts hoặc business module và không được dùng framework delivery, persistence, messaging hay observability.
- Contracts không được phụ thuộc host hoặc business module; chúng là message contract in-process dùng chung với SharedKernel.
- Namespace `.Domain` không được phụ thuộc ASP.NET Core, EF Core, MediatR, Redis, JWT, Serilog, Kafka hoặc Dapr.
- API host chỉ dùng public seam gốc `CoffeeShop.Modules.Counter`; không được dùng namespace implementation `Application`, `Domain`, `Infrastructure` hoặc `.Internal` của Counter.

Architecture test nạp API cùng năm assembly boundary (Counter, Barista, Kitchen, Contracts và SharedKernel) một lần trong `ArchitectureTestContext`. `Assembly.FullName` được dùng cho `ResideInAssembly` vì ArchUnitNET so khớp full assembly name.

## Thử nghiệm mutation có chủ đích

Khi sửa hoặc mở rộng rule module, tạm thêm fixture **và test dưới đây** vào architecture-test assembly, chạy test, rồi xóa lại ngay. Test nạp test assembly riêng, chọn namespace mutation (không chọn `CounterTypes` của production assembly) và gọi đúng reusable rule `ModuleDependencyRules.MustNotDependOn`. Vì vậy fixture thực sự được ArchUnitNET đánh giá.

```csharp
namespace CoffeeShop.Modules.Counter.Mutation;

public sealed class CounterDependsOnBarista
{
    public InitialBaristaModule? Migration;
}
```

Fixture cần import `CoffeeShop.Modules.Barista.Infrastructure.Persistence.Migrations`. Rule Counter → Barista phải báo full type name `CoffeeShop.Modules.Counter.Mutation.CounterDependsOnBarista` cùng reason `Counter must not depend on Barista.`.

```csharp
[Fact]
public void Mutation_fixture_must_be_rejected_by_the_Counter_to_Barista_rule()
{
    var architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(CounterDependsOnBarista).Assembly,
            typeof(BaristaModuleServiceCollectionExtensions).Assembly)
        .Build();

    var exception = Assert.Throws<FailedArchRuleException>(() =>
        ModuleDependencyRules.MustNotDependOn(
                Types().That().ResideInNamespaceMatching("CoffeeShop\\.Modules\\.Counter\\.Mutation"),
                ArchitectureTestContext.BaristaTypes,
                "Counter must not depend on Barista.")
            .Check(architecture));

    Assert.Contains(
        "CoffeeShop.Modules.Counter.Mutation.CounterDependsOnBarista",
        exception.Message,
        StringComparison.Ordinal);
    Assert.Contains("Counter must not depend on Barista.", exception.Message, StringComparison.Ordinal);
}
```

Thêm `using ArchUnitNET.Loader;`, `using ArchUnitNET.xUnit;` và `using CoffeeShop.Modules.Counter.Mutation;` vào file test khi chạy procedure này. Xóa fixture, test tạm và using mutation sau khi đã lưu bằng chứng RED/GREEN.
