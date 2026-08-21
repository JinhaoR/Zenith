namespace Zenith.Core.Navigation;

public sealed record NavigationRequest
{
    public NavigationRequest(string target, NavigationOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
        Origin = origin;
    }

    public string Target { get; }

    public NavigationOrigin Origin { get; }
}
