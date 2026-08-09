using CoffeeShop.Application.Barista;

namespace CoffeeShop.Infrastructure.Time;

public sealed class TaskPreparationDelay : IPreparationDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
