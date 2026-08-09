using CoffeeShop.Application.Common.Time;

namespace CoffeeShop.Infrastructure.Time;

public sealed class TaskPreparationDelay : IPreparationDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
