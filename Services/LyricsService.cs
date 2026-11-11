using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Notch.Services;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <param name="IsSynced">False when the line times are only an even spread over the track.</param>
public sealed record Lyrics(IReadOnlyList<LyricLine> Lines, bool IsSynced);

/// <summary>
/// Finds lyrics on LRCLIB (https://lrclib.net), a free and open lyrics database that
/// needs no account or key. Only the song title and artist are sent.
/// </summary>
public sealed class LyricsService
{
    const string SearchUrl = "https://lrclib.net/api/search";

    static readonly HttpClient Http = CreateClient();

    // [mm:ss.xx] timestamps in LRC files. A line can carry several.
    static readonly Regex TimeTag = new(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]", RegexOptions.Compiled);

    // "(Official Video)", "[Lyrics]", "(Remastered 2011)" and friends, common in browser titles.
    static readonly Regex TitleNoise = new(
        @"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|visuali[sz]er|remaster(ed)?|live|hd|4k|mv)\b[^\)\]]*[\)\]]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex ArtistNoise = new(@"\s*(-\s*topic|vevo)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    readonly Dictionary<string, Lyrics?> _cache = new();

    /// <summary>Returns null when no lyrics exist or the lookup failed.</summary>
    public async Task<Lyrics?> FindAsync(string title, string artist, TimeSpan duration)
    {
        title = TitleNoise.Replace(title, "").Trim();
        artist = ArtistNoise.Replace(artist, "").Trim();
        if (title.Length == 0) return null;

        string key = $"{title}\u0001{artist}".ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            string query = $"?track_name={Uri.EscapeDataString(title)}";
            if (artist.Length > 0) query += $"&artist_name={Uri.EscapeDataString(artist)}";

            var lyrics = Pick(await Http.GetStringAsync(SearchUrl + query), duration);

            // Browser tabs often report a channel name as the artist; try the title alone.
            if (lyrics == null && artist.Length > 0)
                lyrics = Pick(await Http.GetStringAsync($"{SearchUrl}?q={Uri.EscapeDataString(title)}"), duration);

            _cache[key] = lyrics;
            return lyrics;
        }
        catch
        {
            // Offline or the service is down. Not cached, so a later try can succeed.
            return null;
        }
    }

    /// <summary>Chooses the best match from a search result: synced first, closest length first.</summary>
    static Lyrics? Pick(string json, TimeSpan duration)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        string? bestSynced = null, bestPlain = null;
        double bestSyncedGap = double.MaxValue, bestPlainGap = double.MaxValue;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            double length = item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
                ? d.GetDouble()
                : 0;
            double gap = duration > TimeSpan.Zero && length > 0
                ? Math.Abs(length - duration.TotalSeconds)
                : 0;

            string? synced = Text(item, "syncedLyrics");
            if (synced != null && gap < bestSyncedGap)
            {
                bestSynced = synced;
                bestSyncedGap = gap;
            }

            string? plain = Text(item, "plainLyrics");
            if (plain != null && gap < bestPlainGap)
            {
                bestPlain = plain;
                bestPlainGap = gap;
            }
        }

        // Synced lyrics for a different cut of the song would run out of step.
        if (bestSynced != null && bestSyncedGap <= 8)
        {
            var lines = ParseLrc(bestSynced);
            if (lines.Count > 0) return new Lyrics(lines, IsSynced: true);
        }

        if (bestPlain != null)
        {
            var lines = SpreadEvenly(bestPlain, duration);
            if (lines.Count > 0) return new Lyrics(lines, IsSynced: false);
        }

        return null;
    }

    static string? Text(JsonElement item, string name)
        => item.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    static List<LyricLine> ParseLrc(string lrc)
    {
        var lines = new List<LyricLine>();

        foreach (string raw in lrc.Split('\n'))
        {
            var tags = TimeTag.Matches(raw);
            if (tags.Count == 0) continue;

            string text = TimeTag.Replace(raw, "").Trim();
            if (text.Length == 0) text = "♪"; // an instrumental gap

            foreach (Match tag in tags)
            {
                double minutes = double.Parse(tag.Groups[1].Value, CultureInfo.InvariantCulture);
                double seconds = double.Parse(tag.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                lines.Add(new LyricLine(TimeSpan.FromSeconds(minutes * 60 + seconds), text));
            }
        }

        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    /// <summary>Unsynced lyrics get evenly spaced times so the view can still scroll along.</summary>
    static List<LyricLine> SpreadEvenly(string plain, TimeSpan duration)
    {
        var texts = plain.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        double total = duration > TimeSpan.Zero ? duration.TotalSeconds : texts.Count * 3.0;
        var lines = new List<LyricLine>(texts.Count);
        for (int i = 0; i < texts.Count; i++)
            lines.Add(new LyricLine(TimeSpan.FromSeconds(total * i / texts.Count), texts[i]));
        return lines;
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // LRCLIB asks clients to identify themselves.
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Notch/0.2 (Windows notch overlay)");
        return client;
    }
}
