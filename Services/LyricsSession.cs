using System.Windows.Threading;

namespace Notch.Services;

public enum LyricsStatus
{
    /// <summary>The user hasn't turned lyrics on.</summary>
    Closed,
    Loading,
    Ready,
    NotFound,
}

/// <summary>
/// The lyrics of the song that is playing, and which line is being sung right now.
/// Both the music page and the closed notch show lyrics, so the state lives here
/// rather than in either view.
/// </summary>
/// <remarks>Create it on the UI thread; all events are raised there.</remarks>
public sealed class LyricsSession
{
    // Show a line slightly early, so it's readable by the time it is sung.
    static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(250);

    readonly LyricsService _service;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(150) };

    NowPlaying? _np;
    string? _loadingKey;

    public LyricsSession(MediaService media, LyricsService service)
    {
        _service = service;
        _tick.Tick += (_, _) => UpdateLine();
        media.Changed += np => _ui.InvokeAsync(() => OnMediaChanged(np));
    }

    /// <summary>Whether the user has lyrics switched on.</summary>
    public bool IsOpen { get; private set; }

    public LyricsStatus Status { get; private set; } = LyricsStatus.Closed;

    /// <summary>The loaded lyrics, when <see cref="Status"/> is Ready.</summary>
    public Lyrics? Current { get; private set; }

    /// <summary>Index of the line being sung. -1 before the first line.</summary>
    public int LineIndex { get; private set; } = -1;

    /// <summary>The line being sung, or empty before the first one.</summary>
    public string LineText
        => Current != null && LineIndex >= 0 && LineIndex < Current.Lines.Count
            ? Current.Lines[LineIndex].Text
            : "";

    /// <summary>True when there are timed lyrics to follow along with.</summary>
    public bool HasSyncedLyrics => IsOpen && Status == LyricsStatus.Ready && Current is { IsSynced: true };

    /// <summary>Lyrics were switched on or off, or finished loading.</summary>
    public event Action? Changed;

    /// <summary>The song moved on to another line.</summary>
    public event Action? LineChanged;

    public void SetOpen(bool open)
    {
        if (open == IsOpen) return;
        IsOpen = open;

        if (open)
        {
            Load();
            return;
        }

        _tick.Stop();
        _loadingKey = null;
        Current = null;
        LineIndex = -1;
        Status = LyricsStatus.Closed;
        Changed?.Invoke();
    }

    void OnMediaChanged(NowPlaying? np)
    {
        string? previousTrack = _np?.TrackKey;
        _np = np;
        if (!IsOpen) return;

        if (np?.TrackKey != previousTrack)
        {
            Load();
            return;
        }

        // Same song: it was paused, resumed or seeked.
        UpdateTimer();
        UpdateLine();
    }

    async void Load()
    {
        var np = _np;
        Current = null;
        LineIndex = -1;
        _tick.Stop();

        if (np == null)
        {
            _loadingKey = null;
            Status = LyricsStatus.NotFound;
            Changed?.Invoke();
            return;
        }

        string key = np.TrackKey;
        _loadingKey = key;
        Status = LyricsStatus.Loading;
        Changed?.Invoke();

        Lyrics? found = null;
        try
        {
            found = await _service.FindAsync(np.Title, np.Artist, np.Duration);
        }
        catch
        {
            // Treated as not found.
        }

        // Lyrics were closed, or the track changed, while we waited.
        if (!IsOpen || key != _loadingKey) return;

        Current = found;
        Status = found != null ? LyricsStatus.Ready : LyricsStatus.NotFound;
        LineIndex = FindLine();
        Changed?.Invoke();
        UpdateTimer();
    }

    void UpdateTimer()
    {
        bool follow = IsOpen && Status == LyricsStatus.Ready && _np?.IsPlaying == true;
        if (follow) _tick.Start();
        else _tick.Stop();
    }

    void UpdateLine()
    {
        if (Current == null) return;

        int index = FindLine();
        if (index == LineIndex) return;
        LineIndex = index;
        LineChanged?.Invoke();
    }

    int FindLine()
    {
        if (Current == null || _np == null) return -1;

        var time = _np.PositionNow() + Lead;
        var lines = Current.Lines;
        int index = -1;
        for (int i = 0; i < lines.Count && lines[i].Time <= time; i++)
            index = i;
        return index;
    }
}
