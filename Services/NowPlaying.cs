using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Notch.Services;

/// <summary>A snapshot of what the current media app is playing.</summary>
/// <param name="ArtColor">The cover's most vivid colour, or null for a grey cover or none.</param>
/// <param name="Position">Playback position at the moment of <paramref name="LastUpdated"/>.</param>
/// <param name="Duration">Track length. Zero when the app doesn't report a timeline.</param>
/// <param name="StartTime">Timeline offset the app uses; needed when seeking.</param>
public sealed record NowPlaying(
    string Title,
    string Artist,
    bool IsPlaying,
    BitmapImage? Art,
    Color? ArtColor,
    TimeSpan Position,
    TimeSpan Duration,
    TimeSpan StartTime,
    DateTimeOffset LastUpdated)
{
    /// <summary>Identifies the track, so views can tell a new song from a play/pause update.</summary>
    public string TrackKey => $"{Title}\u0001{Artist}";

    /// <summary>
    /// Apps only report the position now and then, so this adds the time that has
    /// passed since the last report.
    /// </summary>
    public TimeSpan PositionNow()
    {
        var position = Position;
        if (IsPlaying && LastUpdated.Year > 2000)
            position += DateTimeOffset.Now - LastUpdated;

        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && position > Duration) position = Duration;
        return position;
    }
}
