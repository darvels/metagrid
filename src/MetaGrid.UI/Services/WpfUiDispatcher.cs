using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace MetaGrid.UI.Services;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private Dispatcher Dispatcher => WpfApplication.Current.Dispatcher;

    public bool CheckAccess() => Dispatcher.CheckAccess();

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        => Dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task;

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
        => Dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task;

    public async Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        => await await Dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);

    public async Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
        => await await Dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
}
