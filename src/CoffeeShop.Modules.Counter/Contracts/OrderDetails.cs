namespace CoffeeShop.Modules.Counter;

public sealed record OrderDetails(Guid OrderId, Guid LoyaltyMemberId, string Status);
