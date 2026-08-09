namespace CoffeeShop.Api.Realtime;

public interface IOrderUpdatesClient
{
    Task ReceiveOrderUpdate(OrderUpdateMessage message);
}
