using CoffeeShop.Domain.Menu;

namespace CoffeeShop.Domain.Orders;

public sealed class LineItem
{
    private LineItem()
    {
    }

    internal LineItem(MenuItem menuItem)
    {
        Id = Guid.NewGuid();
        ItemType = menuItem.Type;
        Name = menuItem.Name;
        Price = menuItem.Price;
        Station = menuItem.Station;
        Status = ItemStatus.InProgress;
    }

    public Guid Id { get; private set; }
    public ItemType ItemType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public PreparationStation Station { get; private set; }
    public ItemStatus Status { get; private set; }
}
