namespace MetaGrid.UI.Services;

public interface IUiDispatcher
{
    bool CheckAccess();
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
    Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
    Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);
    Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);
}
