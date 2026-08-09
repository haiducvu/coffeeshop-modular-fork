namespace CoffeeShop.Application.Common.Time;

public interface IPreparationDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
