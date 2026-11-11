using System.Windows.Media;
using System.Windows.Media.Imaging;
using Notch.Interop;
using Windows.Foundation;
using Windows.Media.Control;

namespace Notch.Services;

/// <summary>
/// Reads whatever Windows shows in its media flyout (Spotify, browsers, most players)
/// through the system media transport controls, and sends play/pause/skip/seek back.
/// </summary>
public sealed class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    int _version;
    string? _artKey;
    BitmapImage? _art;
    Color? _artColor;
    int _artTries;

    // Browsers publish a video's title first and its picture a moment later.
    const int MaxArtTries = 6;
    static readonly TimeSpan ArtRetryDelay = TimeSpan.FromMilliseconds(700);

    /// <summary>Raised on a background thread. Null means nothing is playing.</summary>
    public event Action<NowPlaying?>? Changed;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (manager, _) => Attach(manager.GetCurrentSession());
        Attach(_manager.GetCurrentSession());
    }

    public Task PlayPauseAsync() => RunAsync(s => s.TryTogglePlayPauseAsync());
    public Task NextAsync() => RunAsync(s => s.TrySkipNextAsync());
    public Task PreviousAsync() => RunAsync(s => s.TrySkipPreviousAsync());
    public Task SeekAsync(TimeSpan position) => RunAsync(s => s.TryChangePlaybackPositionAsync(position.Ticks));

    void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }

        _session = session;

        if (session != null)
        {
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            session.TimelinePropertiesChanged += OnTimelineChanged;
        }

        _ = RefreshAsync();
    }

    void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e)
        => _ = RefreshAsync();

    void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
        => _ = RefreshAsync();

    void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e)
        => _ = RefreshAsync();

    async Task RefreshAsync()
    {
        int version = Interlocked.Increment(ref _version);
        var session = _session;
        NowPlaying? result = null;
        bool retryArt = false;

        try
        {
            if (session != null)
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (props != null && !string.IsNullOrWhiteSpace(props.Title))
                {
                    bool playing = session.GetPlaybackInfo()?.PlaybackStatus
                        == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                    // The cover is loaded once per track, not on every play/pause.
                    string key = $"{props.Title}\u0001{props.Artist}\u0001{props.AlbumTitle}";
                    if (key != _artKey)
                    {
                        _artKey = key;
                        _art = null;
                        _artColor = null;
                        _artTries = 0;
                    }

                    // A missing cover is tried again a few times rather than remembered:
                    // YouTube and other browser tabs supply theirs late.
                    if (_art == null && _artTries < MaxArtTries)
                    {
                        _artTries++;
                        _art = await WinRtImage.LoadAsync(props.Thumbnail, decodeWidth: 192);
                        _artColor = AlbumColor.From(_art);
                        retryArt = _art == null && _artTries < MaxArtTries;
                    }

                    var timeline = session.GetTimelineProperties();
                    var start = timeline?.StartTime ?? TimeSpan.Zero;
                    var duration = timeline != null ? timeline.EndTime - start : TimeSpan.Zero;
                    var position = timeline != null ? timeline.Position - start : TimeSpan.Zero;

                    result = new NowPlaying(
                        props.Title,
                        props.Artist ?? "",
                        playing,
                        _art,
                        _artColor,
                        position > TimeSpan.Zero ? position : TimeSpan.Zero,
                        duration > TimeSpan.Zero ? duration : TimeSpan.Zero,
                        start,
                        timeline?.LastUpdatedTime ?? default);
                }
            }
        }
        catch
        {
            // The session can vanish mid-read when an app closes.
        }

        // Drop results that a newer refresh has already superseded.
        if (version == Volatile.Read(ref _version))
        {
            Changed?.Invoke(result);
            if (retryArt) _ = RetryArtAsync(version);
        }
    }

    async Task RetryArtAsync(int version)
    {
        await Task.Delay(ArtRetryDelay);

        // Only if nothing else has refreshed in the meantime.
        if (version == Volatile.Read(ref _version))
            await RefreshAsync();
    }

    async Task RunAsync(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> action)
    {
        var session = _session;
        if (session == null) return;
        try
        {
            await action(session);
        }
        catch
        {
            // The app refused or closed.
        }
    }
}
