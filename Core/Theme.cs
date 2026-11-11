using System.Windows;
using System.Windows.Media;

namespace Notch.Core;

/// <summary>The accent colour, shared by every view through the "AccentBrush" resource.</summary>
public static class Theme
{
    public const string AccentKey = "AccentBrush";
    public const string DefaultAccent = "#30D158";

    /// <summary>Swatches offered in the settings window.</summary>
    public static readonly string[] AccentChoices =
    {
        "#30D158", // green
        "#0A84FF", // blue
        "#BF5AF2", // purple
        "#FF375F", // pink
        "#FF9F0A", // orange
        "#FFFFFF", // white
    };

    public static readonly Brush Warning = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));

    public static bool IsValidColor(string? hex) => TryParse(hex, out _);

    /// <summary>Replaces the app-wide accent brush. Views pick it up through DynamicResource.</summary>
    public static void ApplyAccent(string hex)
    {
        if (!TryParse(hex, out Color color))
            TryParse(DefaultAccent, out color);

        Application.Current.Resources[AccentKey] = Frozen(color);
    }

    static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
            // Not a colour.
        }
        return false;
    }

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
