extern alias BaristaWorker;
extern alias KitchenWorker;

using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using CoffeeShop.Contracts.Menu;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Dapr;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Kitchen;
using CoffeeShop.SharedKernel.Domain;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using BaristaWorkerServiceCollectionExtensions =
    BaristaWorker::CoffeeShop.Barista.Worker.BaristaWorkerServiceCollectionExtensions;
using KitchenWorkerServiceCollectionExtensions =
    KitchenWorker::CoffeeShop.Kitchen.Worker.KitchenWorkerServiceCollectionExtensions;

namespace CoffeeShop.ArchitectureTests;

public sealed class ModuleDependencyTests
{
    [Fact]
    public void Api_must_not_reference_station_runtime_types()
    {
        Assert.DoesNotContain(typeof(Program).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name is "CoffeeShop.Modules.Barista" or "CoffeeShop.Modules.Kitchen");
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.ApiTypes,
                ArchitectureTestContext.BaristaTypes,
                "Only the embedded compatibility composition may reference Barista runtime.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.ApiTypes,
                ArchitectureTestContext.KitchenTypes,
                "Only the embedded compatibility composition may reference Kitchen runtime.")
            .Check(ArchitectureTestContext.Architecture);
    }

    [Fact]
    public void Modules_must_not_depend_on_each_other()
    {
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.CounterTypes,
                ArchitectureTestContext.BaristaTypes,
                "Counter must not depend on Barista.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.CounterTypes,
                ArchitectureTestContext.KitchenTypes,
                "Counter must not depend on Kitchen.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.BaristaTypes,
                ArchitectureTestContext.CounterTypes,
                "Barista must not depend on Counter.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.BaristaTypes,
                ArchitectureTestContext.KitchenTypes,
                "Barista must not depend on Kitchen.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.KitchenTypes,
                ArchitectureTestContext.CounterTypes,
                "Kitchen must not depend on Counter.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.KitchenTypes,
                ArchitectureTestContext.BaristaTypes,
                "Kitchen must not depend on Barista.")
            .Check(ArchitectureTestContext.Architecture);
    }

    [Fact]
    public void SharedKernel_and_Contracts_must_keep_their_dependency_direction()
    {
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.SharedKernelTypes,
                ArchitectureTestContext.ContractsHostAndModuleTypes,
                "SharedKernel must remain independent of the host, modules, and Contracts.")
            .Check(ArchitectureTestContext.Architecture);
        SharedKernelRules.MustBeFrameworkFree(ArchitectureTestContext.SharedKernelTypes)
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.ContractsTypes,
                ArchitectureTestContext.HostAndModuleTypes,
                "Contracts must remain in-process messages independent of the host and business modules.")
            .Check(ArchitectureTestContext.Architecture);
    }

    [Fact]
    public void IntegrationContracts_must_be_broker_and_framework_independent()
    {
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.IntegrationContractTypes,
                ArchitectureTestContext.IntegrationContractForbiddenTypes,
                "IntegrationContracts must remain independent of the host, modules, and in-process contracts.")
            .Check(ArchitectureTestContext.Architecture);
        SharedKernelRules.MustBeFrameworkFree(ArchitectureTestContext.IntegrationContractTypes)
            .Check(ArchitectureTestContext.Architecture);
    }

    [Fact]
    public void Messaging_layers_must_keep_broker_adapters_out_of_contracts_and_modules()
    {
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.MessagingAbstractionTypes,
                ArchitectureTestContext.MessagingAbstractionForbiddenTypes,
                "Messaging abstractions may depend only on IntegrationContracts and BCL types.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.KafkaAdapterTypes,
                ArchitectureTestContext.KafkaAdapterForbiddenTypes,
                "The Kafka adapter must not depend on the API or business modules.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.DaprAdapterTypes,
                ArchitectureTestContext.DaprAdapterForbiddenTypes,
                "The Dapr adapter must not depend on the API, Kafka, or business modules.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.BusinessModuleTypes,
                ArchitectureTestContext.KafkaAdapterTypes,
                "Business modules must not depend on the Kafka adapter.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.BusinessModuleTypes,
                ArchitectureTestContext.DaprAdapterTypes,
                "Business modules must not depend on the Dapr adapter.")
            .Check(ArchitectureTestContext.Architecture);
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.DaprForbiddenConsumerTypes,
                ArchitectureTestContext.DaprFrameworkTypes,
                "Only the API composition root and Dapr adapter may use Dapr framework types.")
            .Check(ArchitectureTestContext.Architecture);
    }

    [Fact]
    public void Barista_worker_must_remain_an_independent_composition_root()
    {
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.BaristaWorkerTypes,
                ArchitectureTestContext.BaristaWorkerForbiddenTypes,
                "Barista Worker must not depend on the API, other business modules, other workers, or Dapr.")
            .Check(ArchitectureTestContext.Architecture);
    }

    [Fact]
    public void Kitchen_worker_must_remain_an_independent_composition_root()
    {
        ModuleDependencyRules.MustNotDependOn(
                ArchitectureTestContext.KitchenWorkerTypes,
                ArchitectureTestContext.KitchenWorkerForbiddenTypes,
                "Kitchen Worker must not depend on the API, other business modules, other workers, or Dapr.")
            .Check(ArchitectureTestContext.Architecture);
    }
}

internal static class ArchitectureTestContext
{
    internal static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(Program).Assembly,
            typeof(ItemType).Assembly,
            typeof(IIntegrationEvent).Assembly,
            typeof(IIntegrationEventPublisher).Assembly,
            typeof(DaprServiceCollectionExtensions).Assembly,
            typeof(KafkaServiceCollectionExtensions).Assembly,
            typeof(BaristaWorkerServiceCollectionExtensions).Assembly,
            typeof(KitchenWorkerServiceCollectionExtensions).Assembly,
            typeof(BaristaModuleServiceCollectionExtensions).Assembly,
            typeof(ICounterModule).Assembly,
            typeof(KitchenModuleServiceCollectionExtensions).Assembly,
            typeof(AggregateRoot).Assembly)
        .Build();

    internal static readonly IObjectProvider<IType> ApiTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(typeof(Program).Assembly));

    internal static readonly IObjectProvider<IType> ContractsTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(typeof(ItemType).Assembly));

    internal static readonly IObjectProvider<IType> IntegrationContractTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(typeof(IIntegrationEvent).Assembly));

    internal static readonly IObjectProvider<IType> MessagingAbstractionTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(typeof(IIntegrationEventPublisher).Assembly));

    internal static readonly IObjectProvider<IType> KafkaAdapterTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(typeof(KafkaServiceCollectionExtensions).Assembly));

    internal static readonly IObjectProvider<IType> DaprAdapterTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(typeof(DaprServiceCollectionExtensions).Assembly));

    internal static readonly IObjectProvider<IType> BaristaWorkerTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(
            typeof(BaristaWorkerServiceCollectionExtensions).Assembly));

    internal static readonly IObjectProvider<IType> KitchenWorkerTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(
            typeof(KitchenWorkerServiceCollectionExtensions).Assembly));

    internal static readonly IObjectProvider<IType> BaristaTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(typeof(BaristaModuleServiceCollectionExtensions).Assembly));

    internal static readonly IObjectProvider<IType> CounterTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(typeof(ICounterModule).Assembly));

    internal static readonly IObjectProvider<IType> KitchenTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(typeof(KitchenModuleServiceCollectionExtensions).Assembly));

    internal static readonly IObjectProvider<IType> SharedKernelTypes = Types().That()
        .ResideInAssembly(FullAssemblyName(typeof(AggregateRoot).Assembly));

    internal static readonly IObjectProvider<IType> HostAndModuleTypes = Types().That()
        .Are(ApiTypes)
        .Or()
        .Are(BaristaWorkerTypes)
        .Or()
        .Are(KitchenWorkerTypes)
        .Or()
        .Are(BaristaTypes)
        .Or()
        .Are(CounterTypes)
        .Or()
        .Are(KitchenTypes);

    internal static readonly IObjectProvider<IType> BusinessModuleTypes = Types().That()
        .Are(BaristaTypes)
        .Or()
        .Are(CounterTypes)
        .Or()
        .Are(KitchenTypes);

    internal static readonly IObjectProvider<IType> ContractsHostAndModuleTypes = Types().That()
        .Are(ContractsTypes)
        .Or()
        .Are(HostAndModuleTypes);

    internal static readonly IObjectProvider<IType> IntegrationContractForbiddenTypes = Types().That()
        .Are(ContractsTypes)
        .Or()
        .Are(SharedKernelTypes)
        .Or()
        .Are(HostAndModuleTypes);

    internal static readonly IObjectProvider<IType> MessagingAbstractionForbiddenTypes = Types().That()
        .Are(ContractsTypes)
        .Or()
        .Are(SharedKernelTypes)
        .Or()
        .Are(DaprAdapterTypes)
        .Or()
        .Are(KafkaAdapterTypes)
        .Or()
        .Are(HostAndModuleTypes);

    internal static readonly IObjectProvider<IType> KafkaAdapterForbiddenTypes = Types().That()
        .Are(ContractsTypes)
        .Or()
        .Are(SharedKernelTypes)
        .Or()
        .Are(HostAndModuleTypes);

    internal static readonly IObjectProvider<IType> DaprAdapterForbiddenTypes = Types().That()
        .Are(ContractsTypes)
        .Or()
        .Are(SharedKernelTypes)
        .Or()
        .Are(KafkaAdapterTypes)
        .Or()
        .Are(HostAndModuleTypes);

    internal static readonly IObjectProvider<IType> DaprForbiddenConsumerTypes = Types().That()
        .Are(ContractsTypes)
        .Or()
        .Are(IntegrationContractTypes)
        .Or()
        .Are(MessagingAbstractionTypes)
        .Or()
        .Are(KafkaAdapterTypes)
        .Or()
        .Are(BaristaWorkerTypes)
        .Or()
        .Are(KitchenWorkerTypes)
        .Or()
        .Are(BusinessModuleTypes)
        .Or()
        .Are(SharedKernelTypes);

    internal static readonly IObjectProvider<IType> DaprFrameworkTypes = Types().That()
        .ResideInNamespaceMatching("Dapr(\\..*)?");

    internal static readonly IObjectProvider<IType> BaristaWorkerForbiddenTypes = Types().That()
        .Are(ApiTypes)
        .Or()
        .Are(KitchenWorkerTypes)
        .Or()
        .Are(CounterTypes)
        .Or()
        .Are(KitchenTypes)
        .Or()
        .Are(DaprAdapterTypes)
        .Or()
        .Are(DaprFrameworkTypes);

    internal static readonly IObjectProvider<IType> KitchenWorkerForbiddenTypes = Types().That()
        .Are(ApiTypes)
        .Or()
        .Are(BaristaWorkerTypes)
        .Or()
        .Are(BaristaTypes)
        .Or()
        .Are(CounterTypes)
        .Or()
        .Are(DaprAdapterTypes)
        .Or()
        .Are(DaprFrameworkTypes);

    internal static readonly IObjectProvider<IType> ForbiddenFrameworkTypes = Types().That()
        .ResideInNamespaceMatching(
            "(Microsoft\\.AspNetCore|Microsoft\\.EntityFrameworkCore|Microsoft\\.Extensions|MediatR|Npgsql|StackExchange\\.Redis|Microsoft\\.IdentityModel|System\\.IdentityModel\\.Tokens\\.Jwt|Serilog|Confluent|OpenTelemetry|Dapr)(\\..*)?");

    private static string FullAssemblyName(System.Reflection.Assembly assembly) => assembly.FullName
        ?? throw new InvalidOperationException($"Assembly '{assembly.GetName().Name}' has no full name.");
}

internal static class ModuleDependencyRules
{
    internal static IArchRule MustNotDependOn(
        IObjectProvider<IType> sourceTypes,
        IObjectProvider<IType> forbiddenTypes,
        string reason) =>
        Types().That().Are(sourceTypes)
            .Should().NotDependOnAny(forbiddenTypes)
            .Because(reason);
}

internal static class SharedKernelRules
{
    internal static IArchRule MustBeFrameworkFree(IObjectProvider<IType> sourceTypes) =>
        Types().That().Are(sourceTypes)
            .Should().NotDependOnAny(ArchitectureTestContext.ForbiddenFrameworkTypes)
            .Because("SharedKernel must remain framework-free.");
}
