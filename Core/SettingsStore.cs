using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notch.Core;

/// <summary>Loads and saves <see cref="AppSettings"/> in %APPDATA%\Notch\settings.json.</summary>
public sealed class SettingsStore
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notch");

    static readonly string FilePath = Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    SettingsStore(AppSettings current) => Current = current;

    public AppSettings Current { get; }

    /// <summary>Raised after every <see cref="Update"/>, on the calling (UI) thread.</summary>
    public event Action? Changed;

    public static SettingsStore Load()
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
        }
        catch
        {
            // Corrupt or unreadable file: fall back to defaults.
        }

        settings ??= new AppSettings();
        settings.Normalize();
        return new SettingsStore(settings);
    }

    /// <summary>Changes one or more settings, saves them and tells listeners.</summary>
    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Current.Normalize();
        Save();
        Changed?.Invoke();
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Json));
        }
        catch
        {
            // Not worth crashing the notch over.
        }
    }
}
