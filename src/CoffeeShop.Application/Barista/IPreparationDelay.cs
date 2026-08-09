namespace CoffeeShop.Application.Barista;

public interface IPreparationDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
