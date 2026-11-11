using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Notch.Core;
using Notch.Services;

namespace Notch.Views;

/// <summary>
/// Now playing: cover, title, progress and controls, with synced lyrics beside them
/// when switched on. Shows a large clock when nothing is playing.
/// </summary>
public partial class MusicPage : NotchPage
{
    const double MediaHeight = 146;
    const double IdleHeight = 98;

    // Column widths. The side margins match MediaPanel's Margin in the XAML.
    const double SideMargins = 40;
    const double PlayerWidth = 400;         // on its own
    const double CompactPlayerWidth = 250;  // with lyrics beside it
    const double LyricsWidth = 330;

    readonly AppServices _app;
    readonly LyricsSession _lyrics;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };

    NowPlaying? _np;
    BitmapImage? _shownArt;
    bool _artShown;
    bool _shown;
    bool _lyricsBeside;

    public MusicPage(AppServices app)
    {
        InitializeComponent();
        _app = app;
        _lyrics = app.LyricsSession;
        IconGlyph = "\uE8D6";

        _tick.Tick += (_, _) => Refresh();

        app.Media.Changed += np => Dispatcher.InvokeAsync(() => Show(np));
        app.Power.Changed += () => Dispatcher.InvokeAsync(UpdateBattery);
        app.Volume.Changed += () => Dispatcher.InvokeAsync(UpdateVolume);
        app.Settings.Changed += ApplySettings;
        _lyrics.Changed += OnLyricsChanged;
        _lyrics.LineChanged += () => LyricsPanel.SetCurrent(_lyrics.LineIndex, animate: true);

        ApplySettings();
        Show(null);
        UpdateClock();
        UpdateBattery();
        UpdateVolume();
    }

    public override void OnShown()
    {
        _shown = true;
        Bars.IsPlaying = _np?.IsPlaying == true;
        Refresh();
        _tick.Start();
    }

    public override void OnHidden()
    {
        _shown = false;
        Bars.IsPlaying = false;
        _tick.Stop();
    }

    void ApplySettings()
    {
        bool lyrics = _app.Settings.Current.Lyrics;
        LyricsButton.Visibility = lyrics ? Visibility.Visible : Visibility.Collapsed;
        if (!lyrics) _lyrics.SetOpen(false);
    }

    void Refresh()
    {
        UpdateClock();
        UpdateProgress();
    }

    // ---------- Now playing ----------

    void Show(NowPlaying? np)
    {
        _np = np;

        MediaPanel.Visibility = np != null ? Visibility.Visible : Visibility.Collapsed;
        IdlePanel.Visibility = np != null ? Visibility.Collapsed : Visibility.Visible;

        bool playing = np?.IsPlaying == true;
        Bars.IsPlaying = _shown && playing;

        if (np != null)
        {
            TitleText.Text = np.Title;
            ArtistText.Text = np.Artist;
            PlayPauseIcon.Data = (Geometry)FindResource(playing ? "PauseGlyph" : "PlayGlyph");
            PlayPauseIcon.Width = playing ? 12 : 13;

            if (!_artShown || !ReferenceEquals(np.Art, _shownArt))
            {
                _artShown = true;
                _shownArt = np.Art;
                Art.Background = np.Art != null
                    ? new ImageBrush(np.Art) { Stretch = Stretch.UniformToFill }
                    : (Brush)FindResource("PlaceholderBrush");
            }
        }

        SetLyricsBeside(_lyrics.IsOpen && np != null, animate: false);
        UpdateProgress();
        UpdateSize();
    }

    void UpdateSize()
    {
        PageWidth = _np != null && _lyricsBeside
            ? SideMargins + CompactPlayerWidth + LyricsWidth
            : DefaultWidth;
        PageHeight = _np == null ? IdleHeight : MediaHeight;
    }

    void UpdateProgress()
    {
        if (_np is not { } np || np.Duration <= TimeSpan.Zero)
        {
            ElapsedText.Text = "";
            RemainingText.Text = "";
            ProgressFill.Width = 0;
            return;
        }

        var position = np.PositionNow();
        ElapsedText.Text = FormatTime(position);
        RemainingText.Text = "-" + FormatTime(np.Duration - position);

        double fill = SeekArea.ActualWidth * position.Ticks / np.Duration.Ticks;
        ProgressFill.Width = fill < 1 ? 0 : Math.Max(4, fill); // keep the rounded ends round
    }

    static string FormatTime(TimeSpan time)
        => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");

    // ---------- Clock and battery ----------

    void UpdateClock()
    {
        var now = DateTime.Now;
        string time = now.ToString("t");
        TimeText.Text = time;
        IdleTime.Text = time;
        DateText.Text = now.ToString("ddd d MMM");
        IdleDate.Text = now.ToString("dddd, d MMMM");
    }

    void UpdateBattery()
    {
        var power = _app.Power.State;
        bool low = power.HasBattery && !power.IsPluggedIn && power.Percent <= 20;
        double level = power.HasBattery ? power.Percent / 100.0 : 1;

        BatteryRing.Value = level;
        BatteryRing.SetWarning(low);
        IdleRing.Value = level;
        IdleRing.SetWarning(low);

        IdleBattery.Text = !power.HasBattery ? ""
            : power.IsCharging ? $"  ·  {power.Percent}%, charging"
            : $"  ·  {power.Percent}%";
    }

    // ---------- Volume ----------

    void UpdateVolume()
        => VolumeIcon.Text = VolumeBanner.GlyphFor(_app.Volume.Level, _app.Volume.IsMuted);

    void Mute_Click(object sender, RoutedEventArgs e) => _app.Volume.ToggleMute();

    // ---------- Lyrics ----------

    void Lyrics_Click(object sender, RoutedEventArgs e) => _lyrics.SetOpen(!_lyrics.IsOpen);

    /// <summary>Lyrics were switched on or off, or finished loading.</summary>
    void OnLyricsChanged()
    {
        bool open = _lyrics.IsOpen;

        LyricsIcon.Opacity = open ? 1 : 0.7;
        if (open)
            LyricsIcon.SetResourceReference(Shape.FillProperty, Theme.AccentKey);
        else
            LyricsIcon.Fill = Brushes.White;

        switch (_lyrics.Status)
        {
            case LyricsStatus.Loading:
                LyricsPanel.ShowStatus("Looking for lyrics…");
                break;
            case LyricsStatus.NotFound:
                LyricsPanel.ShowStatus("No lyrics found for this song");
                break;
            case LyricsStatus.Ready when _lyrics.Current is { } lyrics:
                LyricsPanel.ShowLyrics(lyrics);
                LyricsPanel.SetCurrent(_lyrics.LineIndex, animate: false);
                break;
        }

        SetLyricsBeside(open && _np != null, animate: true);
    }

    /// <summary>Switches between the player on its own and the player with lyrics beside it.</summary>
    void SetLyricsBeside(bool beside, bool animate)
    {
        if (beside == _lyricsBeside) return;
        _lyricsBeside = beside;

        if (!animate || !_shown)
        {
            ApplyLyricsLayout();
            return;
        }

        // The player changes width, which is a real layout change. Doing it while the
        // panel is faded out hides the jump; the notch resizes around it meanwhile.
        Motion.To(MediaPanel, OpacityProperty, 0, 90, completed: () =>
        {
            ApplyLyricsLayout();
            Motion.To(MediaPanel, OpacityProperty, 1, 280);
        });
    }

    void ApplyLyricsLayout()
    {
        bool beside = _lyricsBeside;

        PlayerColumn.Width = new GridLength(beside ? CompactPlayerWidth : PlayerWidth);
        LyricsColumn.Width = new GridLength(beside ? LyricsWidth : 0);
        LyricsSide.Visibility = beside ? Visibility.Visible : Visibility.Collapsed;

        // The narrow player has no room for the date line; the title takes its place.
        Header.Visibility = beside ? Visibility.Collapsed : Visibility.Visible;
        TitleStack.VerticalAlignment = beside ? VerticalAlignment.Center : VerticalAlignment.Bottom;

        UpdateSize();

        // The progress bar's width is only known after the new layout.
        Dispatcher.InvokeAsync(UpdateProgress, DispatcherPriority.Loaded);
    }

    // ---------- Controls ----------

    async void SeekArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_np is not { } np || np.Duration <= TimeSpan.Zero || SeekArea.ActualWidth <= 0) return;

        double fraction = Math.Clamp(e.GetPosition(SeekArea).X / SeekArea.ActualWidth, 0, 1);
        var target = TimeSpan.FromTicks((long)(np.Duration.Ticks * fraction));

        // Move the bar straight away; the app confirms a moment later.
        _np = np with { Position = target, LastUpdated = DateTimeOffset.Now };
        UpdateProgress();

        await _app.Media.SeekAsync(np.StartTime + target);
    }

    async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_np is { } np)
        {
            // Flip the icon immediately rather than waiting for the app to report back.
            Show(np with
            {
                IsPlaying = !np.IsPlaying,
                Position = np.PositionNow(),
                LastUpdated = DateTimeOffset.Now,
            });
        }

        await _app.Media.PlayPauseAsync();
    }

    async void Previous_Click(object sender, RoutedEventArgs e) => await _app.Media.PreviousAsync();

    async void Next_Click(object sender, RoutedEventArgs e) => await _app.Media.NextAsync();
}
