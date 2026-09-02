namespace Zenith.App.Bookmarks;

public sealed record Bookmark(string Title, Uri Target)
{
    public string Host => Target.Host;
}
