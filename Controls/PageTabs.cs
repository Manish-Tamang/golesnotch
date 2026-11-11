using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Notch.Core;

namespace Notch.Controls;

/// <summary>
/// A capsule of page icons plus a settings button. It takes the place of the page dots
/// while the mouse is over them.
/// </summary>
public sealed class PageTabs : Border
{
    const string SettingsGlyph = "\uE713";

    // Icons come from the icon font built into Windows (Fluent on 11, MDL2 on 10).
    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static readonly Brush IdleBrush = Frozen(0x99);
    static readonly Brush HoverBrush = Frozen(0x24);

    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    readonly List<TextBlock> _icons = new();

    public PageTabs()
    {
        Height = 30;
        CornerRadius = new CornerRadius(15);
        Padding = new Thickness(4, 0, 4, 0);
        Background = Frozen(0x1C);
        Child = _row;
    }

    /// <summary>A page icon was clicked. The argument is the page index.</summary>
    public event Action<int>? Selected;

    public event Action? SettingsRequested;

    /// <summary>Rebuilds the row with one icon per page, then the settings button.</summary>
    public void SetPages(IReadOnlyList<string> glyphs)
    {
        _row.Children.Clear();
        _icons.Clear();

        for (int i = 0; i < glyphs.Count; i++)
        {
            int index = i;
            _icons.Add(AddButton(glyphs[i], () => Selected?.Invoke(index)));
        }

        _row.Children.Add(new Rectangle
        {
            Width = 1,
            Height = 14,
            Fill = HoverBrush,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        AddButton(SettingsGlyph, () => SettingsRequested?.Invoke());
    }

    /// <summary>Draws the current page's icon in the accent colour.</summary>
    public void SetActive(int index)
    {
        for (int i = 0; i < _icons.Count; i++)
        {
            if (i == index)
                _icons[i].SetResourceReference(TextBlock.ForegroundProperty, Theme.AccentKey);
            else
                _icons[i].Foreground = IdleBrush;
        }
    }

    TextBlock AddButton(string glyph, Action click)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = IconFont,
            FontSize = 14,
            Foreground = IdleBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var button = new Border
        {
            Width = 34,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = icon,
        };
        button.MouseEnter += (_, _) => button.Background = HoverBrush;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };

        _row.Children.Add(button);
        return icon;
    }

    static Brush Frozen(byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));
        brush.Freeze();
        return brush;
    }
}
