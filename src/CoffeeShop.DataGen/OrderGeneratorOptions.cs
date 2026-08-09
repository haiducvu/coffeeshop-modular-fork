namespace CoffeeShop.DataGen;

public sealed class OrderGeneratorOptions
{
    public const string SectionName = "OrderGenerator";

    public required Uri ApiBaseUrl { get; init; }

    public int OrderCount { get; init; } = 10;

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(1);

    public int Seed { get; init; } = 20260808;
}
