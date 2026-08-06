namespace SMPortal.Services;

// Tiny app-wide notifier. AdminLayout subscribes and renders the toast;
// any component injects IToastService and calls Show(...).
public interface IToastService
{
    string? Current { get; }
    event Action? OnChange;
    void Show(string message);
}

public class ToastService : IToastService
{
    private CancellationTokenSource? _cts;
    public string? Current { get; private set; }
    public event Action? OnChange;

    public void Show(string message)
    {
        Current = message;
        OnChange?.Invoke();

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Delay(2600).ContinueWith(_ =>
        {
            if (token.IsCancellationRequested) return;
            Current = null;
            OnChange?.Invoke();
        }, TaskScheduler.Default);
    }
}
