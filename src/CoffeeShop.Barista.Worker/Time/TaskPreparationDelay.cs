using CoffeeShop.SharedKernel.Time;

namespace CoffeeShop.Barista.Worker.Time;

internal sealed class TaskPreparationDelay : IPreparationDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
