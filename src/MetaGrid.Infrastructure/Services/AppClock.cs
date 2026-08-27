using MetaGrid.Core.Abstractions;

namespace MetaGrid.Infrastructure.Services;

public sealed class AppClock : IAppClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}
