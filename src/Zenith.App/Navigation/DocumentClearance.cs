namespace Zenith.App.Navigation;

/// <summary>
/// Dispatcher-confined removal lifecycle. Hiding or suspending a view is not removal.
/// The scheduler must dispatch on the owning UI thread, like WebView2 callbacks.
/// </summary>
internal sealed class DocumentClearance(
    Func<Action, IDisposable> scheduleTimeout,
    Action destroyController,
    Action<bool> confirmRemoval) : IDisposable
{
    private IDisposable? _timeout;
    private int _generation;
    private bool _finishing;

    public bool IsPending { get; private set; }

    public void Begin()
    {
        if (IsPending) return;
        IsPending = true;
        var generation = ++_generation;
        _timeout = scheduleTimeout(() =>
        {
            if (generation == _generation) Fail();
        });
    }

    public void Issue(Action navigate)
    {
        if (!IsPending) return;
        try { navigate(); }
        catch (Exception) { Fail(); }
    }

    public void Complete(bool succeeded)
    {
        if (!succeeded) { Fail(); return; }
        Finish(destroy: false);
    }

    public void ProcessFailed() => Fail();

    private void Fail() => Finish(destroy: true);

    private void Finish(bool destroy)
    {
        if (!IsPending || _finishing) return;
        _finishing = true;
        try
        {
            // Do not publish removal, or release a deferred navigation, if disposal throws.
            if (destroy) destroyController();
            Dispose();
            IsPending = false;
            confirmRemoval(destroy);
        }
        finally { _finishing = false; }
    }

    public void Dispose()
    {
        ++_generation;
        _timeout?.Dispose();
        _timeout = null;
    }
}
