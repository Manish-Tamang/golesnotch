using Notch.Core;

namespace Notch.Services;

/// <summary>
/// Everything the views need, created once at startup and handed to them.
/// Views never create services themselves.
/// </summary>
public sealed class AppServices
{
    public AppServices()
    {
        LyricsSession = new LyricsSession(Media, Lyrics);
    }

    public SettingsStore Settings { get; } = SettingsStore.Load();
    public MediaService Media { get; } = new();
    public LyricsService Lyrics { get; } = new();
    public LyricsSession LyricsSession { get; }
    public NotificationService Notifications { get; } = new();
    public PowerService Power { get; } = new();
    public DeviceService Devices { get; } = new();
    public VolumeService Volume { get; } = new();
    public RadioService Radios { get; } = new();
    public FocusTimer Timer { get; } = new();

    /// <summary>Starts the services that watch Windows. Call from the UI thread.</summary>
    public async Task StartAsync()
    {
        Power.Start();
        Devices.Start();
        Volume.Start();

        try
        {
            await Media.StartAsync();
        }
        catch
        {
            // Media controls unavailable on this machine; the music page stays idle.
        }

        await Radios.StartAsync();
        await Notifications.StartAsync();
    }
}
