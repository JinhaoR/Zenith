using System.Reflection;

namespace Zenith.Core.Tests;

public sealed class CoreDependencyTests
{
    [Fact]
    public void CoreDoesNotReferenceWpfOrWebView2()
    {
        var referencedAssemblies = Assembly.Load("Zenith.Core")
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name => name is not null)
            .Cast<string>();

        Assert.DoesNotContain(
            referencedAssemblies,
            name => name is "PresentationCore" or "PresentationFramework" or "System.Xaml" or "WindowsBase"
                || name.StartsWith("Microsoft.Web.WebView2", StringComparison.Ordinal));
    }
}
