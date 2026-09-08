namespace Zenith.Core.Navigation;

public abstract record NavigationDecision
{
    private NavigationDecision()
    {
    }

    public sealed record Allowed : NavigationDecision
    {
        public Allowed(Uri target, AccessClass accessClass)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (accessClass is not (AccessClass.Whitelist or AccessClass.Greylist))
            {
                throw new ArgumentOutOfRangeException(nameof(accessClass));
            }
            Target = target;
            AccessClass = accessClass;
        }

        public Uri Target { get; }
        public AccessClass AccessClass { get; }
    }

    public sealed record Denied(NavigationDenialReason Reason) : NavigationDecision;
}
