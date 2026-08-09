using Microsoft.AspNetCore.SignalR;

namespace CoffeeShop.Api.Realtime;

public sealed class OrderUpdatesHub : Hub<IOrderUpdatesClient>;
