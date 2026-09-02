using System.IO;
using System.Text.Json;

namespace Zenith.App.Bookmarks;

public sealed class BookmarkStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public BookmarkStore(string? filePath = null)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            _filePath = filePath;
            return;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Zenith");
        _filePath = Path.Combine(folder, "bookmarks.json");
    }

    public IReadOnlyList<Bookmark> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<Bookmark>>(
                       File.ReadAllText(_filePath),
                       _serializerOptions)
                   ?? [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<Bookmark> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);

        var folder = Path.GetDirectoryName(_filePath);
        if (folder is null)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(bookmarks, _serializerOptions);

        try
        {
            File.WriteAllText(temporaryPath, json);
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
