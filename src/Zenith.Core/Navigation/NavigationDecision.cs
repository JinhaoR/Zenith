namespace Zenith.Core.Navigation;

public abstract record NavigationDecision
{
    private NavigationDecision()
    {
    }

    public sealed record Allowed : NavigationDecision
    {
        public Allowed(Uri target)
        {
            ArgumentNullException.ThrowIfNull(target);
            Target = target;
        }

        public Uri Target { get; }
    }

    public sealed record Denied(NavigationDenialReason Reason) : NavigationDecision;
}
