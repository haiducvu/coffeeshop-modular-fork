namespace CoffeeShop.UnitTests;

public sealed class BootstrapTests
{
    [Fact]
    public void Runtime_targets_dotnet_10()
    {
        Assert.StartsWith("10.", Environment.Version.ToString());
    }
}
