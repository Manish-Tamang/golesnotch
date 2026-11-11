using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Notch.Core;
using Notch.Services;

namespace Notch.Controls;

/// <summary>
/// Scrolling lyrics. The current line sits in the middle, bright; the rest are dimmed
/// and fade out towards the top and bottom edges.
/// </summary>
public sealed class LyricsView : Grid
{
    const double DimOpacity = 0.32;
    const double UnsyncedOpacity = 0.85;

    // The lines live in a Canvas because a Canvas never clips its children to their
    // layout slot. In a normal panel the tall stack would be cut to the visible height
    // first and then scrolled, leaving nothing to scroll into view.
    readonly Canvas _viewport = new() { ClipToBounds = true };
    readonly StackPanel _stack = new();
    readonly TranslateTransform _scroll = new();
    readonly TextBlock _status;

    bool _synced;
    int _current = -1;

    public LyricsView()
    {
        _stack.RenderTransform = _scroll;
        _viewport.Children.Add(_stack);

        _status = new TextBlock
        {
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        Children.Add(_viewport);
        Children.Add(_status);

        // Fade the top and bottom edges so lines drift in and out instead of being cut.
        var fade = new GradientStopCollection
        {
            new GradientStop(Colors.Transparent, 0),
            new GradientStop(Colors.Black, 0.26),
            new GradientStop(Colors.Black, 0.74),
            new GradientStop(Colors.Transparent, 1),
        };
        _viewport.OpacityMask = new LinearGradientBrush(fade, 90);

        SizeChanged += (_, e) =>
        {
            _stack.Width = e.NewSize.Width;
            Dispatcher.InvokeAsync(() => ScrollToCurrent(animate: false), DispatcherPriority.Loaded);
        };
    }

    /// <summary>Replaces the lyrics with a short message ("Looking for lyrics…").</summary>
    public void ShowStatus(string text)
    {
        _stack.Children.Clear();
        _status.Text = text;
        _status.Visibility = Visibility.Visible;
    }

    public void ShowLyrics(Lyrics lyrics)
    {
        _synced = lyrics.IsSynced;
        _current = -1;
        _status.Visibility = Visibility.Collapsed;

        _stack.Children.Clear();
        foreach (var line in lyrics.Lines)
        {
            _stack.Children.Add(new TextBlock
            {
                Text = line.Text,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 5),
                Opacity = _synced ? DimOpacity : UnsyncedOpacity,
            });
        }

        Motion.Set(_scroll, TranslateTransform.YProperty, 0);
    }

    /// <summary>Highlights a line and scrolls it to the middle. -1 means before the first line.</summary>
    public void SetCurrent(int index, bool animate)
    {
        if (_stack.Children.Count == 0) return;

        int previous = _current;
        _current = index;

        // Unsynced lyrics only scroll; highlighting a guessed line would look wrong.
        if (_synced && index != previous)
        {
            SetLineOpacity(previous, DimOpacity, animate);
            SetLineOpacity(index, 1, animate);
        }

        ScrollToCurrent(animate);
    }

    void SetLineOpacity(int index, double opacity, bool animate)
    {
        if (index < 0 || index >= _stack.Children.Count) return;
        var line = _stack.Children[index];

        if (animate)
            Motion.To(line, OpacityProperty, opacity, 320);
        else
            Motion.Set(line, OpacityProperty, opacity);
    }

    void ScrollToCurrent(bool animate)
    {
        if (_stack.Children.Count == 0 || ActualHeight <= 0) return;

        int target = Math.Clamp(_current, 0, _stack.Children.Count - 1);
        _stack.UpdateLayout(); // make sure wrapped line heights are known

        var line = (FrameworkElement)_stack.Children[target];
        double top = line.TranslatePoint(new Point(0, 0), _stack).Y;
        double y = ActualHeight / 2 - (top + line.ActualHeight / 2);

        if (animate)
            Motion.To(_scroll, TranslateTransform.YProperty, y, 620, Motion.Settle());
        else
            Motion.Set(_scroll, TranslateTransform.YProperty, y);
    }
}
