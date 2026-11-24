using System.Windows.Controls;

namespace Notch.Views;

public enum LevelKind { Volume, Brightness }

/// <summary>The popup shown while the volume or the brightness is being changed.</summary>
public partial class VolumeBanner : UserControl
{
    public VolumeBanner()
    {
        InitializeComponent();
        Slider.ValueChanging += level => LevelChanging?.Invoke(Kind, level);
    }

    /// <summary>What the popup is showing now.</summary>
    public LevelKind Kind { get; private set; }

    /// <summary>The user dragged or scrolled the bar.</summary>
    public event Action<LevelKind, double>? LevelChanging;

    /// <summary>Icon-font glyph for a volume level. Also used by the music page's mute button.</summary>
    public static string GlyphFor(double level, bool muted)
        => muted ? ""
         : level < 0.01 ? ""
         : level < 0.34 ? ""
         : level < 0.67 ? ""
         : "";

    public const string BrightnessGlyph = "";

    /// <param name="animate">Glide the bar to the new level instead of jumping.</param>
    public void Set(double level, bool muted, bool animate)
    {
        Kind = LevelKind.Volume;
        Icon.Text = GlyphFor(level, muted);
        Slider.Dimmed = muted;
        Slider.Show(level, animate);
    }

    public void SetBrightness(double level, bool animate)
    {
        Kind = LevelKind.Brightness;
        Icon.Text = BrightnessGlyph;
        Slider.Dimmed = false;
        Slider.Show(level, animate);
    }

    /// <summary>Nudges the level, as scrolling over the popup does.</summary>
    public void Nudge(double step) => Slider.Nudge(step);
}
