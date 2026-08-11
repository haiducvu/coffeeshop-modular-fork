using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace CoffeeShop.ArchitectureTests;

public sealed class DomainPurityTests
{
    [Fact]
    public void Domain_types_must_not_depend_on_delivery_or_infrastructure_frameworks() =>
        Types().That().ResideInNamespaceMatching(".*\\.Domain(\\..*)?")
            .Should().NotDependOnAny(ForbiddenFrameworkTypes)
            .Because("Domain code must remain independent of ASP.NET Core, EF Core, MediatR, Redis, JWT, Serilog, Kafka, and Dapr.")
            .Check(ArchitectureTestContext.Architecture);

    private static readonly IObjectProvider<IType> ForbiddenFrameworkTypes =
        ArchitectureTestContext.ForbiddenFrameworkTypes;
}
