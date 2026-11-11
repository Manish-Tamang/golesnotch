using System.Windows.Controls;
using System.Windows.Media;

namespace Notch.Views;

/// <summary>The small status popup: charger connected, low battery, timer finished.</summary>
public partial class HudBanner : UserControl
{
    public HudBanner()
    {
        InitializeComponent();
    }

    /// <param name="icon">
    /// Either the resource key of an icon shape in Themes/Notch.xaml ("BoltGlyph"), or a
    /// single character from Windows' icon font.
    /// </param>
    /// <param name="ring">How full the ring around the icon is, 0 to 1.</param>
    /// <param name="warning">Draws the ring in red.</param>
    /// <param name="value">Large text on the right, such as "70%". Can be empty.</param>
    public void Show(string icon, double ring, bool warning, string title, string subtitle, string value)
    {
        bool fromFont = icon.Length == 1;
        Glyph.Data = fromFont ? null : (Geometry)FindResource(icon);
        FontGlyph.Text = fromFont ? icon : "";
        Ring.Value = ring;
        Ring.SetWarning(warning);
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        ValueText.Text = value;
    }
}
