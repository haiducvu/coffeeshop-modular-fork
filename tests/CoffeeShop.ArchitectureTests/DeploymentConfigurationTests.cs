using System.Text.RegularExpressions;

namespace CoffeeShop.ArchitectureTests;

public sealed partial class DeploymentConfigurationTests
{
    [Fact]
    public void Dapr_Kafka_starts_new_consumer_groups_at_the_oldest_offset()
    {
        var repositoryRoot = FindRepositoryRoot();
        var component = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "deploy",
            "dapr",
            "components",
            "pubsub.yaml"));

        Assert.Matches(InitialOffsetPattern(), component);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "CoffeeShop.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("CoffeeShop repository root was not found.");
    }

    [GeneratedRegex(
        @"(?m)^\s*- name:\s*initialOffset\s*$\r?\n^\s*value:\s*oldest\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex InitialOffsetPattern();
}
