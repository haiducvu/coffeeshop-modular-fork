using CoffeeShop.SharedKernel.Time;

namespace CoffeeShop.Kitchen.Worker.Time;

internal sealed class TaskPreparationDelay : IPreparationDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
