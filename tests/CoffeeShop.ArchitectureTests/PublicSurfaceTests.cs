using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace CoffeeShop.ArchitectureTests;

public sealed class PublicSurfaceTests
{
    [Fact]
    public void Host_must_not_depend_on_Counter_implementation_namespaces() =>
        Types().That().Are(ArchitectureTestContext.ApiTypes)
            .Should().NotDependOnAny(CounterImplementationTypes)
            .Because("The host must use the public CoffeeShop.Modules.Counter interface namespace, not Counter implementation namespaces.")
            .Check(ArchitectureTestContext.Architecture);

    private static readonly IObjectProvider<IType> CounterImplementationTypes = Types().That()
        .ResideInNamespaceMatching(
            "CoffeeShop\\.Modules\\.Counter\\.(Application|Domain|Infrastructure|Internal)(\\..*)?");
}
