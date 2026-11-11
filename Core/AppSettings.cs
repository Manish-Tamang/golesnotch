namespace Notch.Core;

public enum NotchStyle { Floating, Classic }
public enum OpenMode { Hover, Click }
public enum AnimationSpeed { Relaxed, Normal, Fast }

/// <summary>Everything the user can change. Saved as JSON by <see cref="SettingsStore"/>.</summary>
public sealed class AppSettings
{
    public const double MinWidth = 130;
    public const double MaxWidth = 230;
    public const double MaxTopGap = 20;

    // Appearance
    public NotchStyle Style { get; set; } = NotchStyle.Floating;
    /// <summary>Width of the closed notch, in device-independent pixels.</summary>
    public double CollapsedWidth { get; set; } = 168;
    /// <summary>Distance below the top edge in floating style.</summary>
    public double TopGap { get; set; } = 8;
    /// <summary>Accent colour as a hex string, e.g. #30D158.</summary>
    public string Accent { get; set; } = Theme.DefaultAccent;

    // Behaviour
    public OpenMode Open { get; set; } = OpenMode.Hover;
    public AnimationSpeed Speed { get; set; } = AnimationSpeed.Normal;
    public bool HideInFullscreen { get; set; } = true;
    /// <summary>Hovering the page dots swaps them for a row of page icons.</summary>
    public bool PageIcons { get; set; } = true;

    // Features
    public bool ShowNotificationsPage { get; set; } = true;
    public bool ShowTimerPage { get; set; } = true;
    public bool ShowDevicesPage { get; set; } = true;
    public bool Lyrics { get; set; } = true;
    /// <summary>Tints the notch with the album cover's colour while music shows.</summary>
    public bool AlbumGlow { get; set; } = true;
    public bool NotificationPopups { get; set; } = true;
    public bool PowerPopups { get; set; } = true;
    public bool VolumePopups { get; set; } = true;
    /// <summary>Popups when a USB drive, phone or Bluetooth device connects or disconnects.</summary>
    public bool DevicePopups { get; set; } = true;

    /// <summary>Pulls hand-edited or outdated values back into their valid ranges.</summary>
    public void Normalize()
    {
        if (double.IsNaN(CollapsedWidth)) CollapsedWidth = 168;
        if (double.IsNaN(TopGap)) TopGap = 8;
        CollapsedWidth = Math.Clamp(CollapsedWidth, MinWidth, MaxWidth);
        TopGap = Math.Clamp(TopGap, 0, MaxTopGap);
        if (!Theme.IsValidColor(Accent)) Accent = Theme.DefaultAccent;
    }
}
