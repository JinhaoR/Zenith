using System.IO;
using System.Text.Json;

namespace Zenith.App.Settings;

public sealed class BrowserPreferencesStore(string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zenith", "preferences.json");

    public BrowserPreferences Load()
    {
        try
        {
            var preferences = JsonSerializer.Deserialize<BrowserPreferences>(File.ReadAllText(_filePath));
            return preferences is { IsValid: true } ? preferences : new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(BrowserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.IsValid)
        {
            throw new ArgumentException("Unsupported default page zoom.", nameof(preferences));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
