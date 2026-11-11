using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Notch.Core;
using Notch.Interop;
using Notch.Services;

namespace Notch.Views;

/// <summary>
/// The settings window. Each control writes its setting as soon as it changes, and the
/// notch updates live through <see cref="SettingsStore.Changed"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    readonly SettingsStore _store;

    // True while controls are being filled in, so that doesn't count as the user changing them.
    bool _loading = true;

    public SettingsWindow(SettingsStore store)
    {
        InitializeComponent();
        _store = store;

        SourceInitialized += (_, _) => Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);

        BuildAccentSwatches();
        LoadValues();
        NavAppearance.IsChecked = true;
        _loading = false;
    }

    void LoadValues()
    {
        var s = _store.Current;

        StyleFloating.IsChecked = s.Style == NotchStyle.Floating;
        StyleClassic.IsChecked = s.Style == NotchStyle.Classic;
        WidthSlider.Value = s.CollapsedWidth;
        GapSlider.Value = s.TopGap;
        UpdateAppearanceLabels();

        OpenHover.IsChecked = s.Open == OpenMode.Hover;
        OpenClick.IsChecked = s.Open == OpenMode.Click;
        SpeedRelaxed.IsChecked = s.Speed == AnimationSpeed.Relaxed;
        SpeedNormal.IsChecked = s.Speed == AnimationSpeed.Normal;
        SpeedFast.IsChecked = s.Speed == AnimationSpeed.Fast;
        FullscreenToggle.IsChecked = s.HideInFullscreen;
        PageIconsToggle.IsChecked = s.PageIcons;
        StartupToggle.IsChecked = StartupRegistration.IsEnabled;

        NotificationsPageToggle.IsChecked = s.ShowNotificationsPage;
        TimerPageToggle.IsChecked = s.ShowTimerPage;
        DevicesPageToggle.IsChecked = s.ShowDevicesPage;
        LyricsToggle.IsChecked = s.Lyrics;
        AlbumGlowToggle.IsChecked = s.AlbumGlow;
        NotificationPopupsToggle.IsChecked = s.NotificationPopups;
        PowerPopupsToggle.IsChecked = s.PowerPopups;
        DevicePopupsToggle.IsChecked = s.DevicePopups;
        VolumePopupsToggle.IsChecked = s.VolumePopups;

        string version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        VersionRow.Description = $"Version {version}";
    }

    void UpdateAppearanceLabels()
    {
        WidthLabel.Text = $"{WidthSlider.Value:0} px";
        GapLabel.Text = $"{GapSlider.Value:0} px";
        GapRow.IsEnabled = StyleFloating.IsChecked == true; // the gap only applies to floating
    }

    void BuildAccentSwatches()
    {
        foreach (string hex in Theme.AccentChoices)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Tag = hex,
                IsChecked = string.Equals(hex, _store.Current.Accent, StringComparison.OrdinalIgnoreCase),
            };
            swatch.Click += (_, _) => _store.Update(s => s.Accent = hex);
            AccentSwatches.Children.Add(swatch);
        }
    }

    // ---------- Sidebar ----------

    void Nav_Click(object sender, RoutedEventArgs e)
    {
        AppearancePanel.Visibility = Shown(NavAppearance);
        BehaviourPanel.Visibility = Shown(NavBehaviour);
        FeaturesPanel.Visibility = Shown(NavFeatures);
        AboutPanel.Visibility = Shown(NavAbout);
    }

    static Visibility Shown(RadioButton nav)
        => nav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    // ---------- Appearance ----------

    void Style_Click(object sender, RoutedEventArgs e)
    {
        _store.Update(s => s.Style = StyleClassic.IsChecked == true ? NotchStyle.Classic : NotchStyle.Floating);
        UpdateAppearanceLabels();
    }

    void Width_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _store.Update(s => s.CollapsedWidth = Math.Round(e.NewValue));
        UpdateAppearanceLabels();
    }

    void Gap_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _store.Update(s => s.TopGap = Math.Round(e.NewValue));
        UpdateAppearanceLabels();
    }

    // ---------- Behaviour ----------

    void Open_Click(object sender, RoutedEventArgs e)
        => _store.Update(s => s.Open = OpenClick.IsChecked == true ? OpenMode.Click : OpenMode.Hover);

    void Speed_Click(object sender, RoutedEventArgs e)
        => _store.Update(s => s.Speed =
            SpeedRelaxed.IsChecked == true ? AnimationSpeed.Relaxed
            : SpeedFast.IsChecked == true ? AnimationSpeed.Fast
            : AnimationSpeed.Normal);

    void Startup_Click(object sender, RoutedEventArgs e)
    {
        StartupRegistration.Set(StartupToggle.IsChecked == true);
        StartupToggle.IsChecked = StartupRegistration.IsEnabled; // show what actually happened
    }

    // ---------- On/off switches ----------

    /// <summary>All the simple switches share this: read every one and save.</summary>
    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _store.Update(s =>
        {
            s.HideInFullscreen = FullscreenToggle.IsChecked == true;
            s.PageIcons = PageIconsToggle.IsChecked == true;
            s.ShowNotificationsPage = NotificationsPageToggle.IsChecked == true;
            s.ShowTimerPage = TimerPageToggle.IsChecked == true;
            s.ShowDevicesPage = DevicesPageToggle.IsChecked == true;
            s.Lyrics = LyricsToggle.IsChecked == true;
            s.AlbumGlow = AlbumGlowToggle.IsChecked == true;
            s.NotificationPopups = NotificationPopupsToggle.IsChecked == true;
            s.PowerPopups = PowerPopupsToggle.IsChecked == true;
            s.DevicePopups = DevicePopupsToggle.IsChecked == true;
            s.VolumePopups = VolumePopupsToggle.IsChecked == true;
        });
    }

    // ---------- About ----------

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            Process.Start(new ProcessStartInfo(SettingsStore.Folder) { UseShellExecute = true });
        }
        catch
        {
            // Explorer couldn't open it.
        }
    }

    void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
