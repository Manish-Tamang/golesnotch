using System.Windows.Controls;
using Notch.Core;

namespace Notch.Views;

/// <summary>The popup shown while the volume is being changed.</summary>
public partial class VolumeBanner : UserControl
{
    // The bar's width before the first layout: the popup's width less margins and icon.
    const double InitialTrackWidth = 300;

    public VolumeBanner()
    {
        InitializeComponent();
    }

    /// <summary>Icon-font glyph for a volume level. Also used by the music page's mute button.</summary>
    public static string GlyphFor(double level, bool muted)
        => muted ? "\uE74F"
         : level < 0.01 ? "\uE992"
         : level < 0.34 ? "\uE993"
         : level < 0.67 ? "\uE994"
         : "\uE995";

    /// <param name="animate">Glide the bar to the new level instead of jumping.</param>
    public void Set(double level, bool muted, bool animate)
    {
        Icon.Text = GlyphFor(level, muted);
        Fill.Opacity = muted ? 0.4 : 1;

        double track = Track.ActualWidth > 0 ? Track.ActualWidth : InitialTrackWidth;
        double width = track * Math.Clamp(level, 0, 1);

        if (animate)
            Motion.To(Fill, WidthProperty, width, 200);
        else
            Motion.Set(Fill, WidthProperty, width);
    }
}
