using CoffeeShop.SharedKernel.Time;

namespace CoffeeShop.Api.Time;

public sealed class TaskPreparationDelay : IPreparationDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
