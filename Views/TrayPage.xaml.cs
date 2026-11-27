using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ManagedShell.WindowsTray;
using Notch.Interop;
using Notch.Services;
using NativeRect = ManagedShell.Interop.NativeMethods.Rect;

namespace Notch.Views;

/// <summary>
/// The system tray's app icons, for desktops where the taskbar (and so the tray) is hidden.
/// Clicks go to the app exactly as Explorer's tray would send them.
/// </summary>
public partial class TrayPage : NotchPage
{
    const double Cell = 42;
    const double IconSize = 20;
    const int MaxRows = 3;
    const double HeaderHeight = 42;
    const double BottomPadding = 12;

    readonly TrayIconService _tray;
    readonly DispatcherTimer _closeAfterClick = new();
    bool _visible;
    bool _stale = true;

    public TrayPage(AppServices app)
    {
        InitializeComponent();
        IconGlyph = "\uE70E";
        _tray = app.Tray;
        _tray.Changed += () =>
        {
            _stale = true;
            if (_visible) Rebuild();
        };
        _closeAfterClick.Tick += (_, _) =>
        {
            _closeAfterClick.Stop();
            IconActivated?.Invoke();
        };
        Rebuild();
    }

    /// <summary>An icon was clicked; the app is about to show a menu or window, so the notch should close.</summary>
    public event Action? IconActivated;

    public override void OnShown()
    {
        _visible = true;
        if (_stale) Rebuild();
    }

    public override void OnHidden()
    {
        _visible = false;
        HintText.Text = "";
    }

    void Rebuild()
    {
        _stale = false;
        IconRow.Children.Clear();

        var icons = _tray.Icons;
        int perRow = Math.Max(1, (int)((PageWidth - 28) / Cell));
        foreach (var icon in icons.Take(perRow * MaxRows))
            IconRow.Children.Add(MakeCell(icon));

        EmptyText.Visibility = icons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IconRow.Visibility = icons.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        int rows = Math.Min(MaxRows, (icons.Count + perRow - 1) / perRow);
        PageHeight = icons.Count == 0 ? HeaderHeight + 52 : HeaderHeight + rows * Cell + BottomPadding;
    }

    FrameworkElement MakeCell(NotifyIcon icon)
    {
        var image = new Image
        {
            Source = icon.Icon,
            Width = IconSize,
            Height = IconSize,
            Stretch = Stretch.Uniform,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var cell = new Border
        {
            Width = Cell - 4,
            Height = Cell - 4,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Background = Brushes.Transparent,
            Child = image,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(),
        };

        cell.MouseEnter += (_, _) =>
        {
            cell.Background = HoverBrush;
            HintText.Text = icon.Title ?? "";
        };
        cell.MouseLeave += (_, _) =>
        {
            cell.Background = Brushes.Transparent;
            SetPressed(cell, false);
            HintText.Text = "";
        };
        cell.MouseDown += (_, e) =>
        {
            e.Handled = true;
            SetPressed(cell, true);
            SetPlacement(icon, cell);
            icon.IconMouseDown(e.ChangedButton, CursorParam(), (int)Native.GetDoubleClickTime());
        };
        cell.MouseUp += (_, e) =>
        {
            e.Handled = true;
            SetPressed(cell, false);
            icon.IconMouseUp(e.ChangedButton, CursorParam(), (int)Native.GetDoubleClickTime());

            // A left click may be the first half of a double click, so wait before closing.
            _closeAfterClick.Stop();
            if (e.ChangedButton == MouseButton.Left)
            {
                _closeAfterClick.Interval = TimeSpan.FromMilliseconds(Native.GetDoubleClickTime() + 50);
                _closeAfterClick.Start();
            }
            else
            {
                IconActivated?.Invoke();
            }
        };

        return cell;
    }

    static readonly Brush HoverBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)));

    static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    static void SetPressed(Border cell, bool pressed)
    {
        if (cell.RenderTransform is ScaleTransform scale)
            scale.ScaleX = scale.ScaleY = pressed ? 0.9 : 1;
    }

    /// <summary>The pointer in screen pixels, packed the way tray messages carry it.</summary>
    static uint CursorParam()
    {
        if (!Native.TryGetCursorPos(out var p)) return 0;
        return ((uint)(int)p.Y << 16) | ((uint)(int)p.X & 0xFFFF);
    }

    /// <summary>Apps that ask where their icon is (to place a flyout next to it) get the cell's position.</summary>
    static void SetPlacement(NotifyIcon icon, FrameworkElement cell)
    {
        if (PresentationSource.FromVisual(cell) == null) return;
        var topLeft = cell.PointToScreen(new Point(0, 0));
        var bottomRight = cell.PointToScreen(new Point(cell.ActualWidth, cell.ActualHeight));
        icon.Placement = new NativeRect((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
    }
}
