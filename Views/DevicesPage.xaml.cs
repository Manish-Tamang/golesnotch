using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Notch.Core;
using Notch.Services;
using Windows.Devices.Radios;

namespace Notch.Views;

/// <summary>
/// Wi-Fi and Bluetooth switches, and the connected drives, phones and Bluetooth devices
/// with Open and Eject. It covers what the taskbar's quick settings and "Safely remove
/// hardware" icon are normally for.
/// </summary>
public partial class DevicesPage : NotchPage
{
    const double HeaderHeight = 42;
    const double TilesHeight = 60;
    const double RowHeight = 46;
    const double EmptyHeight = 30;
    const double BottomPadding = 6;
    const int MaxRows = 4;

    static readonly Brush OffBrush = CreateOffBrush();

    readonly DeviceService _devices;
    readonly RadioService _radios;

    public DevicesPage(AppServices app)
    {
        InitializeComponent();
        IconGlyph = "\uE772";
        _devices = app.Devices;
        _radios = app.Radios;

        _devices.Changed += () => Dispatcher.InvokeAsync(Rebuild);
        _radios.Changed += () => Dispatcher.InvokeAsync(UpdateRadios);
        Rebuild();
    }

    /// <summary>Icon-font glyph for a kind of device. Also used by the connect popup.</summary>
    public static string GlyphFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Drive => "\uE88E",
        DeviceKind.Portable => "\uE8EA",
        _ => "\uE702",
    };

    // The Wi-Fi network may have changed since the page was last open.
    public override void OnShown() => UpdateRadios();

    // ---------- Wi-Fi and Bluetooth ----------

    void UpdateRadios()
    {
        bool? wifi = _radios.IsOn(RadioKind.WiFi);
        PaintDot(WifiDot, wifi == true);
        WifiStatus.Text = wifi switch
        {
            null => "Not available",
            false => "Off",
            true => _radios.WifiNetwork ?? "On",
        };

        bool? bluetooth = _radios.IsOn(RadioKind.Bluetooth);
        PaintDot(BluetoothDot, bluetooth == true);
        BluetoothStatus.Text = bluetooth switch
        {
            null => "Not available",
            false => "Off",
            true => _devices.Devices.FirstOrDefault(device => device.Kind == DeviceKind.Bluetooth)?.Name ?? "On",
        };
    }

    /// <summary>The round icon is filled with the accent colour while its radio is on.</summary>
    static void PaintDot(Border dot, bool on)
    {
        if (on)
            dot.SetResourceReference(Border.BackgroundProperty, Theme.AccentKey);
        else
            dot.Background = OffBrush;
    }

    async void Wifi_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await ToggleAsync(RadioKind.WiFi);
    }

    async void Bluetooth_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await ToggleAsync(RadioKind.Bluetooth);
    }

    async Task ToggleAsync(RadioKind kind)
    {
        if (_radios.IsOn(kind) is not { } on) return;
        await _radios.SetAsync(kind, !on);
        UpdateRadios();
    }

    // Windows' list of Wi-Fi networks, the one the taskbar's network icon opens.
    void WifiMore_Click(object sender, RoutedEventArgs e) => OpenLink("ms-availablenetworks:");

    void BluetoothMore_Click(object sender, RoutedEventArgs e) => OpenLink("ms-settings:bluetooth");

    static void OpenLink(string link)
    {
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch
        {
            // That part of Windows is missing or blocked.
        }
    }

    // ---------- Connected devices ----------

    void Rebuild()
    {
        var devices = _devices.Devices.Take(MaxRows).ToList();

        DeviceList.Children.Clear();
        foreach (var device in devices)
            DeviceList.Children.Add(CreateRow(device));

        EmptyText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageHeight = HeaderHeight + TilesHeight
                   + (devices.Count == 0 ? EmptyHeight : devices.Count * RowHeight)
                   + BottomPadding;

        UpdateRadios(); // the Bluetooth tile names the connected device
    }

    FrameworkElement CreateRow(ConnectedDevice device)
    {
        var row = new Grid { Height = RowHeight };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = OffBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = GlyphFor(device.Kind),
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = device.Name,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = device.Kind switch
            {
                DeviceKind.Drive => "USB drive",
                DeviceKind.Portable => "Portable device",
                _ => "Bluetooth",
            },
            FontSize = 12,
            Foreground = (Brush)FindResource("MutedBrush"),
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(CreateButton("Open", "PillButtonSecondary", () => DeviceService.Open(device)));
        if (device.Kind == DeviceKind.Drive)
            buttons.Children.Add(CreateButton("Eject", "PillButton", () => DeviceService.Eject(device)));

        Grid.SetColumn(text, 1);
        Grid.SetColumn(buttons, 2);
        row.Children.Add(icon);
        row.Children.Add(text);
        row.Children.Add(buttons);
        return row;
    }

    Button CreateButton(string label, string styleKey, Action click)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource(styleKey),
            Margin = new Thickness(8, 0, 0, 0),
        };
        button.Click += (_, _) => click();
        return button;
    }

    static Brush CreateOffBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        brush.Freeze();
        return brush;
    }
}
