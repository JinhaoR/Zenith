using Zenith.App.Navigation;

namespace Zenith.App.Tests.Navigation;

public sealed class DocumentClearanceTests
{
    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("hung")]
    [InlineData("renderer-crash")]
    [InlineData("navigate-threw")]
    public void RemovalIsConfirmedOnlyAfterControllerDestruction(string failure)
    {
        var events = new List<string>();
        var deadline = new ManualDeadline();
        using var clear = new DocumentClearance(deadline.Schedule,
            () => events.Add("destroy"), destroyed => events.Add(destroyed ? "removed-destroyed" : "removed-blank"));
        clear.Begin();
        clear.Issue(() => { });
        Assert.True(clear.IsPending);
        Assert.Empty(events);

        switch (failure)
        {
            case "failed":
            case "cancelled": clear.Complete(false); break;
            case "hung": deadline.Fire(); break;
            case "renderer-crash": clear.ProcessFailed(); break;
            case "navigate-threw": clear.Issue(() => throw new InvalidOperationException()); break;
        }

        Assert.Equal(["destroy", "removed-destroyed"], events);
        Assert.False(clear.IsPending);
        Assert.True(deadline.Cancelled);
        // Late completions, crash notifications and already-queued timer callbacks
        // cannot report a second removal or revive the old controller.
        clear.Complete(true);
        clear.ProcessFailed();
        deadline.Fire();
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void SuccessfulClearCancelsDeadlineWithoutDestroyingController()
    {
        var deadline = new ManualDeadline();
        var removed = false;
        using var clear = new DocumentClearance(deadline.Schedule,
            () => Assert.Fail("Successful clear must preserve the controller"),
            destroyed => { Assert.False(destroyed); removed = true; });
        clear.Begin();
        Assert.False(removed);
        clear.Complete(true);
        deadline.Fire();
        Assert.True(removed);
        Assert.False(clear.IsPending);
    }

    [Fact]
    public void DisposalFailureNeverReportsRemoval()
    {
        var deadline = new ManualDeadline();
        using var clear = new DocumentClearance(deadline.Schedule,
            () => throw new InvalidOperationException("dispose failed"),
            _ => Assert.Fail("Content removal has not been established"));
        clear.Begin();
        Assert.Throws<InvalidOperationException>(() => clear.Complete(false));
        Assert.True(clear.IsPending);
    }

    [Fact]
    public void OldDeadlineCannotDestroyANewerDocument()
    {
        var deadline = new ManualDeadline();
        using var clear = new DocumentClearance(deadline.Schedule,
            () => Assert.Fail("Stale timeout"), _ => { });
        clear.Begin();
        var oldCallback = deadline.Callback;
        clear.Complete(true);
        clear.Begin();
        oldCallback!();
        Assert.True(clear.IsPending);
        clear.Complete(true);
    }

    private sealed class ManualDeadline : IDisposable
    {
        public Action? Callback { get; private set; }
        public bool Cancelled { get; private set; }
        public IDisposable Schedule(Action callback) { Callback = callback; Cancelled = false; return this; }
        public void Fire() => Callback!();
        public void Dispose() => Cancelled = true;
    }
}
