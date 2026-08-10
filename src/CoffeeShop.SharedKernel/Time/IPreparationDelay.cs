namespace CoffeeShop.SharedKernel.Time;

public interface IPreparationDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
