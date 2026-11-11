using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Notch.Services;

namespace Notch.Views;

/// <summary>A focus countdown with a few preset lengths.</summary>
public partial class TimerPage : NotchPage
{
    readonly FocusTimer _timer;

    public TimerPage(AppServices app)
    {
        InitializeComponent();
        IconGlyph = "\uE916";
        _timer = app.Timer;
        _timer.Changed += Refresh;

        PageHeight = 124;
        SelectPreset();
        Refresh();
    }

    public static string Format(TimeSpan time)
    {
        // Round up, so a fresh 25-minute session reads 25:00 rather than 24:59.
        var rounded = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, time.TotalSeconds)));
        return rounded.TotalHours >= 1 ? rounded.ToString(@"h\:mm\:ss") : rounded.ToString(@"mm\:ss");
    }

    void Refresh()
    {
        TimeText.Text = Format(_timer.Remaining);
        Ring.Value = _timer.Duration > TimeSpan.Zero
            ? _timer.Remaining.TotalSeconds / _timer.Duration.TotalSeconds
            : 0;

        StartButton.Content = _timer.IsRunning ? "Pause" : _timer.IsPaused ? "Resume" : "Start";
        StateText.Text = _timer.IsRunning ? "Focusing" : _timer.IsPaused ? "Paused" : "Focus timer";
    }

    /// <summary>Ticks the chip that matches the timer's current length.</summary>
    void SelectPreset()
    {
        string minutes = ((int)_timer.Duration.TotalMinutes).ToString(CultureInfo.InvariantCulture);
        foreach (var chip in Presets.Children.OfType<RadioButton>())
            chip.IsChecked = (string)chip.Tag == minutes;
    }

    void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string minutes })
            _timer.SetDuration(TimeSpan.FromMinutes(int.Parse(minutes, CultureInfo.InvariantCulture)));
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_timer.IsRunning) _timer.Pause();
        else _timer.Start();
    }

    void Reset_Click(object sender, RoutedEventArgs e) => _timer.Reset();
}
