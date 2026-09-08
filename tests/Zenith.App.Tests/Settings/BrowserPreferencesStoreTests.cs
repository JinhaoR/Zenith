using Zenith.App.Settings;

namespace Zenith.App.Tests.Settings;

public sealed class BrowserPreferencesStoreTests
{
    [Fact]
    public void PreferencesSurviveReloadAndCanBeChanged()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-PreferencesTests-");
        try
        {
            var path = Path.Combine(folder.FullName, "preferences.json");
            var store = new BrowserPreferencesStore(path);
            Assert.Equal(new BrowserPreferences(), store.Load());
            var saved = new BrowserPreferences(false, 125);
            store.Save(saved);
            Assert.Equal(saved, new BrowserPreferencesStore(path).Load());
            store.Save(new BrowserPreferences());
            Assert.Equal(new BrowserPreferences(), store.Load());
            Assert.Single(folder.GetFiles());
        }
        finally { folder.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"DefaultZoomPercent\":0}")]
    [InlineData("{\"DefaultZoomPercent\":1000000}")]
    public void DamagedPreferencesUseSafePresentationDefaults(string json)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-PreferencesTests-");
        try
        {
            var path = Path.Combine(folder.FullName, "preferences.json");
            File.WriteAllText(path, json);
            Assert.Equal(new BrowserPreferences(), new BrowserPreferencesStore(path).Load());
        }
        finally { folder.Delete(recursive: true); }
    }
}
