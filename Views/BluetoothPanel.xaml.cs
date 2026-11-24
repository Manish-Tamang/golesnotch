using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Notch.Core;
using Notch.Services;
using Windows.Devices.Radios;

namespace Notch.Views;

/// <summary>
/// Bluetooth inside the notch: paired devices with Connect, Disconnect and Remove, and a
/// list of nearby devices to pair with. Opened from the arrow on the Bluetooth tile.
/// </summary>
public partial class BluetoothPanel : UserControl
{
    const double HeaderHeight = 42;
    const double RowHeight = 46;
    const double MaxListHeight = 232;

    static readonly Brush OffBrush = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));

    enum PromptKind { Confirm, Show }
    sealed record Prompt(string DeviceId, string Code, PromptKind Kind, TaskCompletionSource<bool>? Answer);

    readonly BluetoothService _bluetooth;
    readonly RadioService _radios;
    readonly Dictionary<string, string> _status = new();
    bool _open;
    bool _adding;
    string? _confirmRemove;
    DateTime _confirmRemoveAt;
    Prompt? _prompt;

    public BluetoothPanel(AppServices app)
    {
        InitializeComponent();
        _bluetooth = app.Bluetooth;
        _radios = app.Radios;
        _bluetooth.Changed += () => Dispatcher.InvokeAsync(Rebuild);
        _radios.Changed += () => Dispatcher.InvokeAsync(Rebuild);
    }

    public double PanelHeight { get; private set; } = HeaderHeight + RowHeight;

    public event Action? HeightChanged;
    public event Action? BackRequested;

    public void Open()
    {
        _open = true;
        _adding = false;
        Rebuild();
    }

    public void Close()
    {
        _open = false;
        SetAdding(false);
        _prompt?.Answer?.TrySetResult(false);
        _prompt = null;
        _confirmRemove = null;
    }

    void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    void Add_Click(object sender, RoutedEventArgs e) => SetAdding(!_adding);

    void SetAdding(bool adding)
    {
        _adding = adding;
        if (adding) _bluetooth.StartScan();
        else _bluetooth.StopScan();
        Rebuild();
    }

    void Rebuild()
    {
        if (!_open) return;

        List.Children.Clear();
        bool? radioOn = _radios.IsOn(RadioKind.Bluetooth);
        TitleText.Text = _adding ? "Add a device" : "Bluetooth";
        AddButton.Content = _adding ? "Done" : "Add device";
        AddButton.Visibility = radioOn == false ? Visibility.Collapsed : Visibility.Visible;

        if (radioOn == false)
        {
            var on = PillButton("Turn on", primary: true, async () => await _radios.SetAsync(RadioKind.Bluetooth, true));
            List.Children.Add(Message("Bluetooth is off", on));
        }
        else if (_adding)
        {
            var nearby = _bluetooth.Nearby;
            List.Children.Add(Note(nearby.Count == 0
                ? "Looking for devices. Put yours in pairing mode."
                : "Looking for devices…"));
            foreach (var device in nearby) List.Children.Add(NearbyRow(device));
        }
        else
        {
            var paired = _bluetooth.Paired;
            if (paired.Count == 0) List.Children.Add(Message("No paired devices", null));
            foreach (var device in paired) List.Children.Add(PairedRow(device));
        }

        List.Measure(new Size(Width > 0 ? Width : NotchPage.DefaultWidth, double.PositiveInfinity));
        PanelHeight = HeaderHeight + Math.Min(MaxListHeight, Math.Ceiling(List.DesiredSize.Height));
        HeightChanged?.Invoke();
    }

    // ---------- Paired devices ----------

    FrameworkElement PairedRow(BluetoothDeviceItem device)
    {
        string status = _status.GetValueOrDefault(device.Id)
                     ?? (device.IsConnected ? "Connected" : "Paired");
        var buttons = new List<Button>();

        if (device.HasAudio && !_status.ContainsKey(device.Id))
        {
            buttons.Add(PillButton(device.IsConnected ? "Disconnect" : "Connect", primary: false,
                                   () => SetConnected(device, !device.IsConnected)));
        }

        bool confirming = _confirmRemove == device.Id && DateTime.UtcNow - _confirmRemoveAt < TimeSpan.FromSeconds(4);
        if (!_status.ContainsKey(device.Id))
        {
            buttons.Add(PillButton(confirming ? "Remove?" : "Remove", primary: confirming, () =>
            {
                if (confirming) return Remove(device);
                _confirmRemove = device.Id;
                _confirmRemoveAt = DateTime.UtcNow;
                Rebuild();
                return Task.CompletedTask;
            }));
        }

        return Row(GlyphFor(device.Kind), device.IsConnected, device.Name, status, buttons);
    }

    async Task SetConnected(BluetoothDeviceItem device, bool connect)
    {
        _status[device.Id] = connect ? "Connecting…" : "Disconnecting…";
        Rebuild();
        bool sent = await _bluetooth.SetAudioConnectedAsync(device, connect);
        _status.Remove(device.Id);
        if (!sent) _status[device.Id] = connect ? "Couldn't connect" : "Couldn't disconnect";
        Rebuild();
        if (!sent) ClearStatusLater(device.Id);
    }

    async Task Remove(BluetoothDeviceItem device)
    {
        _confirmRemove = null;
        _status[device.Id] = "Removing…";
        Rebuild();
        bool removed = await _bluetooth.UnpairAsync(device.Id);
        _status.Remove(device.Id);
        if (!removed)
        {
            _status[device.Id] = "Couldn't remove";
            ClearStatusLater(device.Id);
        }
        Rebuild();
    }

    // ---------- Pairing ----------

    FrameworkElement NearbyRow(BluetoothDeviceItem device)
    {
        if (_prompt is { } prompt && prompt.DeviceId == device.Id)
        {
            if (prompt.Kind == PromptKind.Show)
                return Row(GlyphFor(device.Kind), false, device.Name, $"Type {prompt.Code} on the device, then Enter", new());

            return Row(GlyphFor(device.Kind), false, device.Name, $"Does it show {prompt.Code}?", new()
            {
                PillButton("Cancel", primary: false, () => Answer(false)),
                PillButton("Pair", primary: true, () => Answer(true)),
            });
        }

        string? status = _status.GetValueOrDefault(device.Id);
        var buttons = status == null
            ? new List<Button> { PillButton("Pair", primary: true, () => Pair(device)) }
            : status.StartsWith("Pair it in", StringComparison.Ordinal)
                ? new List<Button> { PillButton("Settings", primary: false, OpenSettings) }
                : new List<Button>();

        return Row(GlyphFor(device.Kind), false, device.Name, status ?? KindName(device.Kind), buttons);
    }

    Task Answer(bool accept)
    {
        _prompt?.Answer?.TrySetResult(accept);
        _prompt = null;
        Rebuild();
        return Task.CompletedTask;
    }

    async Task Pair(BluetoothDeviceItem device)
    {
        _status[device.Id] = "Pairing…";
        Rebuild();

        var outcome = await _bluetooth.PairAsync(
            device.Id,
            code => Dispatcher.InvokeAsync(() =>
            {
                var answer = new TaskCompletionSource<bool>();
                _prompt = new Prompt(device.Id, code, PromptKind.Confirm, answer);
                Rebuild();
                return answer.Task;
            }).Task.Unwrap(),
            code => Dispatcher.InvokeAsync(() =>
            {
                _prompt = new Prompt(device.Id, code, PromptKind.Show, null);
                Rebuild();
            }));

        _prompt = null;
        _status.Remove(device.Id);
        switch (outcome)
        {
            case PairOutcome.Paired:
                SetAdding(false);
                return;
            case PairOutcome.NeedsSettings:
                _status[device.Id] = "Pair it in Windows Settings";
                break;
            case PairOutcome.Failed:
                _status[device.Id] = "Couldn't pair. Try again";
                ClearStatusLater(device.Id);
                break;
        }
        Rebuild();
    }

    static Task OpenSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
        }
        catch
        {
        }
        return Task.CompletedTask;
    }

    async void ClearStatusLater(string id)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (_status.Remove(id)) Rebuild();
    }

    // ---------- Building blocks ----------

    public static string GlyphFor(BluetoothKind kind) => kind switch
    {
        BluetoothKind.Audio => "",
        BluetoothKind.Input => "",
        BluetoothKind.Phone => "",
        BluetoothKind.Computer => "",
        _ => "",
    };

    static string KindName(BluetoothKind kind) => kind switch
    {
        BluetoothKind.Audio => "Audio",
        BluetoothKind.Input => "Mouse or keyboard",
        BluetoothKind.Phone => "Phone",
        BluetoothKind.Computer => "Computer",
        _ => "Bluetooth",
    };

    FrameworkElement Row(string glyph, bool active, string name, string status, List<Button> buttons)
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
                Text = glyph,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        if (active) icon.SetResourceReference(Border.BackgroundProperty, Theme.AccentKey);

        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = status,
            FontSize = 12,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in buttons) actions.Children.Add(button);

        Grid.SetColumn(text, 1);
        Grid.SetColumn(actions, 2);
        row.Children.Add(icon);
        row.Children.Add(text);
        row.Children.Add(actions);
        return row;
    }

    Button PillButton(string label, bool primary, Func<Task> click)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource(primary ? "PillButton" : "PillButtonSecondary"),
            Margin = new Thickness(8, 0, 0, 0),
        };
        button.Click += async (_, _) => await click();
        return button;
    }

    FrameworkElement Message(string text, Button? action)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = (Brush)FindResource("MutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (action != null)
        {
            action.HorizontalAlignment = HorizontalAlignment.Center;
            action.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(action);
        }
        return panel;
    }

    FrameworkElement Note(string text) => new TextBlock
    {
        Text = text,
        FontSize = 12,
        Foreground = (Brush)FindResource("MutedBrush"),
        Margin = new Thickness(2, 0, 0, 6),
    };

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
