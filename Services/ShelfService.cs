using System.IO;
using System.Text.Json;
using Notch.Core;

namespace Notch.Services;

/// <summary>A file or folder sitting on the shelf.</summary>
public sealed record ShelfItem(string Path)
{
    public string Name
    {
        get
        {
            string name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
            return name.Length > 0 ? name : Path; // a whole drive, such as D:\
        }
    }

    public bool IsFolder => Directory.Exists(Path);

    public bool Exists => File.Exists(Path) || Directory.Exists(Path);
}

/// <summary>
/// The file shelf: files dragged onto the notch, kept until they're dragged out again or
/// removed. Only the paths are kept, never copies of the files. The list survives a
/// restart in %APPDATA%\Notch\shelf.json. Used from the UI thread only.
/// </summary>
public sealed class ShelfService
{
    static readonly string FilePath = Path.Combine(SettingsStore.Folder, "shelf.json");

    readonly List<ShelfItem> _items = new();

    public ShelfService()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) is { } paths)
                _items.AddRange(paths.Select(path => new ShelfItem(path)));
        }
        catch
        {
            // Unreadable list: start with an empty shelf.
        }

        Prune();
    }

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<ShelfItem> Items => _items;

    public event Action? Changed;

    /// <summary>Adds files and folders, skipping any already on the shelf. Returns how many were new.</summary>
    public int Add(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (_items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            _items.Add(new ShelfItem(path));
            added++;
        }

        if (added > 0) Save();
        return added;
    }

    public void Remove(ShelfItem item)
    {
        if (_items.Remove(item)) Save();
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Save();
    }

    /// <summary>Drops items whose file was moved or deleted (for example, dragged out into another folder).</summary>
    public void Prune()
    {
        if (_items.RemoveAll(item => !item.Exists) > 0) Save();
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items.Select(item => item.Path).ToList()));
        }
        catch
        {
            // The shelf still works for this session.
        }

        Changed?.Invoke();
    }
}
