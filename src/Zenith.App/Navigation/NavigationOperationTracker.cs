namespace Zenith.App.Navigation;

internal enum NavigationCompletionKind
{
    Stale,
    InternalClear,
    External
}

internal enum ExternalNavigationDisposition
{
    Start,
    DeferUntilInternalClearCompletes
}

internal readonly record struct NavigationOperationCompletion(
    NavigationCompletionKind Kind,
    Uri? RequestedTarget = null,
    Uri? DeferredTarget = null);

internal sealed class NavigationOperationTracker
{
    private InternalClearStage _internalClearStage;
    private ulong? _internalClearNavigationId;
    private ulong? _externalNavigationId;
    private Uri? _externalRequestedTarget;
    private Uri? _deferredExternalTarget;

    public bool IsInternalClearPending => _internalClearStage != InternalClearStage.None;

    public static bool IsBlankTarget(string target) =>
        string.Equals(target, "about:blank", StringComparison.OrdinalIgnoreCase);

    public bool ScheduleInternalClear()
    {
        AbandonExternal();
        _deferredExternalTarget = null;

        if (IsInternalClearPending)
        {
            return false;
        }

        _internalClearStage = InternalClearStage.Scheduled;
        return true;
    }

    public bool TryIssueInternalClear()
    {
        if (_internalClearStage != InternalClearStage.Scheduled)
        {
            return false;
        }

        _internalClearStage = InternalClearStage.Issued;
        return true;
    }

    public bool TryRecordInternalClearStarting(string target, ulong navigationId)
    {
        if (_internalClearStage != InternalClearStage.Issued ||
            !IsBlankTarget(target))
        {
            return false;
        }

        _internalClearNavigationId = navigationId;
        return true;
    }

    public ExternalNavigationDisposition PrepareExternal(Uri requestedTarget)
    {
        ArgumentNullException.ThrowIfNull(requestedTarget);
        AbandonExternal();

        if (_internalClearStage == InternalClearStage.Scheduled)
        {
            _internalClearStage = InternalClearStage.None;
            return ExternalNavigationDisposition.Start;
        }

        if (_internalClearStage == InternalClearStage.Issued)
        {
            _deferredExternalTarget = requestedTarget;
            return ExternalNavigationDisposition.DeferUntilInternalClearCompletes;
        }

        return ExternalNavigationDisposition.Start;
    }

    public void RecordExternal(ulong navigationId, Uri requestedTarget)
    {
        ArgumentNullException.ThrowIfNull(requestedTarget);

        _externalNavigationId = navigationId;
        _externalRequestedTarget = requestedTarget;
    }

    public void AbandonExternal()
    {
        _externalNavigationId = null;
        _externalRequestedTarget = null;
    }

    public Uri? FailInternalClear()
    {
        _internalClearStage = InternalClearStage.None;
        _internalClearNavigationId = null;
        return TakeDeferredTarget();
    }

    public NavigationOperationCompletion MatchCompletion(ulong navigationId)
    {
        if (_internalClearNavigationId == navigationId)
        {
            _internalClearStage = InternalClearStage.None;
            _internalClearNavigationId = null;
            return new NavigationOperationCompletion(
                NavigationCompletionKind.InternalClear,
                DeferredTarget: TakeDeferredTarget());
        }

        if (_externalNavigationId != navigationId)
        {
            return new NavigationOperationCompletion(NavigationCompletionKind.Stale);
        }

        var requestedTarget = _externalRequestedTarget;
        _externalNavigationId = null;
        _externalRequestedTarget = null;
        return new NavigationOperationCompletion(
            NavigationCompletionKind.External,
            requestedTarget);
    }

    private Uri? TakeDeferredTarget()
    {
        var target = _deferredExternalTarget;
        _deferredExternalTarget = null;
        return target;
    }

    private enum InternalClearStage
    {
        None,
        Scheduled,
        Issued
    }
}
