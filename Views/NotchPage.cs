using System.Windows;
using System.Windows.Controls;

namespace Notch.Views;

/// <summary>
/// Base class for a page inside the open notch (music, notifications, timer).
/// A page lays itself out once at its own size; the notch window grows its black body
/// to <see cref="PageWidth"/> × <see cref="PageHeight"/> around it.
/// </summary>
public class NotchPage : UserControl
{
    public const double DefaultWidth = 440;

    double _pageWidth = DefaultWidth;
    double _pageHeight;

    public NotchPage()
    {
        Width = DefaultWidth;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>Glyph from Windows' icon font, shown in the page icon row.</summary>
    public string IconGlyph { get; protected set; } = "";

    /// <summary>How wide the notch should be while this page is showing.</summary>
    public double PageWidth
    {
        get => _pageWidth;
        protected set
        {
            if (value == _pageWidth) return;
            _pageWidth = value;
            Width = value;
            PageSizeChanged?.Invoke(this);
        }
    }

    /// <summary>How tall the notch should be while this page is showing.</summary>
    public double PageHeight
    {
        get => _pageHeight;
        protected set
        {
            if (value == _pageHeight) return;
            _pageHeight = value;
            PageSizeChanged?.Invoke(this);
        }
    }

    public event Action<NotchPage>? PageSizeChanged;

    /// <summary>The notch opened on this page, or the user switched to it.</summary>
    public virtual void OnShown() { }

    /// <summary>The notch closed, or the user switched away. Stop timers and animations here.</summary>
    public virtual void OnHidden() { }
}
