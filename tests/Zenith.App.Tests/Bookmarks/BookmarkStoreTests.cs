using Zenith.App.Bookmarks;

namespace Zenith.App.Tests.Bookmarks;

public sealed class BookmarkStoreTests
{
    [Fact]
    public void SaveAndLoadRoundTripsBookmarks()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ZenithTests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "bookmarks.json");

        try
        {
            var bookmark = new Bookmark("GitHub", new Uri("https://github.com/openai/"));
            var store = new BookmarkStore(path);

            store.Save([bookmark]);

            var loaded = Assert.Single(store.Load());
            Assert.Equal(bookmark.Title, loaded.Title);
            Assert.Equal(bookmark.Target, loaded.Target);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void SavingAChangedCollectionPersistsBookmarkRemoval()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ZenithTests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "bookmarks.json");

        try
        {
            var github = new Bookmark("GitHub", new Uri("https://github.com/"));
            var wikipedia = new Bookmark("Wikipedia", new Uri("https://wikipedia.org/"));
            var store = new BookmarkStore(path);

            store.Save([github, wikipedia]);
            store.Save([wikipedia]);

            var loaded = Assert.Single(store.Load());
            Assert.Equal(wikipedia, loaded);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
