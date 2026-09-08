using Zenith.App.Navigation;

namespace Zenith.App.Tests.Navigation;

public sealed class NavigationOperationTrackerTests
{
    [Fact]
    public void ExternalNavigationIsDeferredBehindAnIssuedInternalClear()
    {
        var tracker = new NavigationOperationTracker();
        var target = new Uri("https://www.wikipedia.org/");

        Assert.True(tracker.ScheduleInternalClear());
        Assert.True(tracker.TryIssueInternalClear());
        Assert.Equal(
            ExternalNavigationDisposition.DeferUntilInternalClearCompletes,
            tracker.PrepareExternal(target));
        Assert.True(tracker.TryRecordInternalClearStarting("about:blank", 41));

        var clearCompletion = tracker.MatchCompletion(41);

        Assert.Equal(NavigationCompletionKind.InternalClear, clearCompletion.Kind);
        Assert.Equal(target, clearCompletion.DeferredTarget);
        Assert.False(tracker.IsInternalClearPending);
    }

    [Fact]
    public void ExternalNavigationCancelsAnInternalClearThatHasNotBeenIssued()
    {
        var tracker = new NavigationOperationTracker();
        var target = new Uri("https://www.wikipedia.org/");

        Assert.True(tracker.ScheduleInternalClear());

        Assert.Equal(ExternalNavigationDisposition.Start, tracker.PrepareExternal(target));
        Assert.False(tracker.IsInternalClearPending);
        Assert.False(tracker.TryIssueInternalClear());
    }

    [Fact]
    public void OnlyIssuedAboutBlankCanStartAnInternalClear()
    {
        var tracker = new NavigationOperationTracker();

        Assert.False(tracker.TryRecordInternalClearStarting("about:blank", 40));
        Assert.True(tracker.ScheduleInternalClear());
        Assert.True(tracker.TryIssueInternalClear());
        Assert.False(tracker.TryRecordInternalClearStarting("https://example.com/", 41));
        Assert.True(tracker.TryRecordInternalClearStarting("about:blank", 42));
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("ABOUT:BLANK")]
    public void BlankTargetRecognitionIsCaseInsensitive(string target)
    {
        Assert.True(NavigationOperationTracker.IsBlankTarget(target));
    }

    [Fact]
    public void SupersededExternalCompletionIsIgnored()
    {
        var tracker = new NavigationOperationTracker();
        var firstTarget = new Uri("https://github.com/");
        var latestTarget = new Uri("https://www.wikipedia.org/");

        tracker.RecordExternal(41, firstTarget);
        tracker.RecordExternal(42, latestTarget);

        var staleCompletion = tracker.MatchCompletion(41);
        var latestCompletion = tracker.MatchCompletion(42);

        Assert.Equal(NavigationCompletionKind.Stale, staleCompletion.Kind);
        Assert.Equal(NavigationCompletionKind.External, latestCompletion.Kind);
        Assert.Equal(latestTarget, latestCompletion.RequestedTarget);
    }

    [Fact]
    public void AbandonedExternalCompletionIsIgnored()
    {
        var tracker = new NavigationOperationTracker();
        tracker.RecordExternal(41, new Uri("https://github.com/"));

        tracker.AbandonExternal();

        Assert.Equal(
            NavigationCompletionKind.Stale,
            tracker.MatchCompletion(41).Kind);
    }
}
