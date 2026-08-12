using Microsoft.AspNetCore.Authorization;

namespace CoffeeShop.Api.Authorization;

public sealed class OrderOwnerRequirement : IAuthorizationRequirement;
