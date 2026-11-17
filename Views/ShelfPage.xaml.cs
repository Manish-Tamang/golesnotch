using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Notch.Core;
using Notch.Interop;
using Notch.Services;

namespace Notch.Views;

/// <summary>
/// The file shelf. Files dragged onto the notch land here (the notch window handles the
/// drop); each one can be dragged back out into a chat, an email or a folder, or clicked
/// to open it.
/// </summary>
public partial class ShelfPage : NotchPage
{
    const double TileWidth = 74;
    const double TileGap = 4;
    const double ThumbnailSize = 46;
    const int ThumbnailPixels = 96;   // sharp at 200% display scaling

    static readonly Brush OutlineIdle = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    static readonly Brush TileHover = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    static readonly Brush RemoveBackground = Frozen(Color.FromRgb(0x3A, 0x3A, 0x3C));

    readonly ShelfService _shelf;
    readonly TranslateTransform _scroll = new();
    readonly Dictionary<string, ImageSource> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _shownPaths = new(StringComparer.OrdinalIgnoreCase);

    double _scrollTarget;
    bool _highlight;

    // A press that may turn into a drag or a click.
    FrameworkElement? _pressed;
    Point _pressPoint;

    public ShelfPage(AppServices app)
    {
        InitializeComponent();
        IconGlyph = "\uE7B8";
        PageHeight = 136;
        _shelf = app.Shelf;

        ItemRow.RenderTransform = _scroll;
        _shelf.Changed += Rebuild;

        HookPress(DragAllHandle,
                  paths: () => _shelf.Items.Select(item => item.Path).ToArray(),
                  click: null);
        DragAllHandle.MouseEnter += (_, _) => DragAllText.Foreground = Brushes.White;
        DragAllHandle.MouseLeave += (_, _) => DragAllText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        Rebuild();
    }

    /// <summary>The notch window starts and ends a drag of these files out of the notch.</summary>
    public event Action<bool>? DraggingOutChanged;

    // Files may have been moved or deleted while the notch was closed.
    public override void OnShown() => _shelf.Prune();

    /// <summary>Something is being dragged over the notch: invite the drop.</summary>
    public void SetDropHighlight(bool on)
    {
        if (on == _highlight) return;
        _highlight = on;
        UpdateDropZone(animate: true);

        if (on)
        {
            // A small nudge on the arrow, so the notch visibly reacts.
            Motion.Set(DropIconShift, TranslateTransform.YProperty, -6);
            Motion.To(DropIconShift, TranslateTransform.YProperty, 0, 520, Motion.Spring());
        }
    }

    // ---------- Building the row ----------

    void Rebuild()
    {
        var items = _shelf.Items;
        ItemRow.Children.Clear();

        foreach (var item in items)
        {
            var tile = CreateTile(item);
            ItemRow.Children.Add(tile);

            // Newly dropped files pop in; the rest just appear where they were.
            if (!_shownPaths.Contains(item.Path)) PopIn(tile);
        }

        _shownPaths.Clear();
        foreach (var item in items) _shownPaths.Add(item.Path);

        CountText.Text = items.Count switch
        {
            0 => "",
            1 => "1 item",
            _ => $"{items.Count} items",
        };
        DragAllHandle.Visibility = items.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = items.Count >= 1 ? Visibility.Visible : Visibility.Collapsed;

        UpdateDropZone(animate: false);

        // Keep the scroll position valid, and show the newest file if the row overflows.
        Dispatcher.InvokeAsync(() => ScrollTo(double.NegativeInfinity, animate: true),
                               System.Windows.Threading.DispatcherPriority.Loaded);
    }

    FrameworkElement CreateTile(ShelfItem item)
    {
        var tile = new Grid
        {
            Width = TileWidth,
            Height = 84,
            Margin = new Thickness(0, 0, TileGap, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };

        var hover = new Border { CornerRadius = new CornerRadius(12) };
        tile.Children.Add(hover);

        var stack = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
        tile.Children.Add(stack);

        // Thumbnail, with the file-type glyph until it has loaded.
        var picture = new Grid { Width = ThumbnailSize, Height = ThumbnailSize };
        var glyph = new TextBlock
        {
            Text = item.IsFolder ? "\uE8B7" : "\uE8A5",
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 26,
            Foreground = (Brush)FindResource("MutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var image = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        picture.Children.Add(glyph);
        picture.Children.Add(image);
        stack.Children.Add(picture);

        if (_thumbnails.TryGetValue(item.Path, out var cached))
        {
            image.Source = cached;
            glyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            LoadThumbnail(item.Path, image, glyph);
        }

        stack.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontSize = 11,
            Margin = new Thickness(5, 5, 5, 0),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = 13.5,
            MaxHeight = 27,
        });

        // A small × in the corner, shown on hover, takes the file off the shelf.
        var remove = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = RemoveBackground,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 6, 0),
            Opacity = 0,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "\uE711",
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        remove.MouseLeftButtonDown += (_, e) => e.Handled = true; // not the start of a drag
        remove.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _shelf.Remove(item);
        };
        tile.Children.Add(remove);

        tile.MouseEnter += (_, _) =>
        {
            hover.Background = TileHover;
            remove.IsHitTestVisible = true;
            Motion.To(remove, OpacityProperty, 1, 140);
        };
        tile.MouseLeave += (_, _) =>
        {
            hover.Background = null;
            remove.IsHitTestVisible = false;
            Motion.To(remove, OpacityProperty, 0, 120);
        };

        HookPress(tile, paths: () => new[] { item.Path }, click: () => Open(item));
        return tile;
    }

    async void LoadThumbnail(string path, Image image, UIElement glyph)
    {
        var source = await Task.Run(() => ShellThumbnail.Get(path, ThumbnailPixels));
        if (source == null) return;

        _thumbnails[path] = source;
        image.Source = source;
        glyph.Visibility = Visibility.Collapsed;
        Motion.Set(image, OpacityProperty, 0);
        Motion.To(image, OpacityProperty, 1, 200);
    }

    static void PopIn(FrameworkElement tile)
    {
        var scale = (ScaleTransform)tile.RenderTransform;
        Motion.Set(tile, OpacityProperty, 0);
        Motion.Set(scale, ScaleTransform.ScaleXProperty, 0.6);
        Motion.Set(scale, ScaleTransform.ScaleYProperty, 0.6);
        Motion.To(tile, OpacityProperty, 1, 200, delayMs: 60);
        Motion.To(scale, ScaleTransform.ScaleXProperty, 1, 560, Motion.Spring(), delayMs: 60);
        Motion.To(scale, ScaleTransform.ScaleYProperty, 1, 560, Motion.Spring(), delayMs: 60);
    }

    void UpdateDropZone(bool animate)
    {
        bool empty = _shelf.Items.Count == 0;
        bool show = empty || _highlight;

        DropTitle.Text = _highlight ? "Drop to keep it here" : "Drop files here";
        if (_highlight)
        {
            DropOutline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, Theme.AccentKey);
            DropIcon.SetResourceReference(TextBlock.ForegroundProperty, Theme.AccentKey);
        }
        else
        {
            DropOutline.Stroke = OutlineIdle;
            DropIcon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        }

        if (animate)
            Motion.To(DropZone, OpacityProperty, show ? 1 : 0, show ? 160 : 220);
        else
            Motion.Set(DropZone, OpacityProperty, show ? 1 : 0);
    }

    // ---------- Scrolling the row ----------

    void Row_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only once the row overflows; otherwise the wheel switches pages as usual.
        if (MaxScroll() <= 0) return;
        e.Handled = true;
        ScrollTo(_scrollTarget + e.Delta * 0.6, animate: true);
    }

    double MaxScroll() => Math.Max(0, ItemRow.ActualWidth - TileGap - RowViewport.ActualWidth);

    /// <summary>Scrolls the row; offsets are zero or negative. Out-of-range values are clamped.</summary>
    void ScrollTo(double offset, bool animate)
    {
        _scrollTarget = Math.Clamp(offset, -MaxScroll(), 0);
        if (animate)
            Motion.To(_scroll, TranslateTransform.XProperty, _scrollTarget, 320);
        else
            Motion.Set(_scroll, TranslateTransform.XProperty, _scrollTarget);
    }

    // ---------- Click to open, drag to take out ----------

    /// <summary>
    /// A press on <paramref name="element"/> becomes a drag of <paramref name="paths"/>
    /// once the mouse moves a few pixels, or a <paramref name="click"/> if it doesn't.
    /// </summary>
    void HookPress(FrameworkElement element, Func<string[]> paths, Action? click)
    {
        element.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _pressed = element;
            _pressPoint = e.GetPosition(this);
            element.CaptureMouse();
        };

        element.MouseMove += (_, e) =>
        {
            if (_pressed != element || e.LeftButton != MouseButtonState.Pressed) return;

            var moved = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _pressed = null;
            element.ReleaseMouseCapture();
            DragOut(element, paths());
        };

        element.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            bool wasPressed = _pressed == element;
            _pressed = null;
            element.ReleaseMouseCapture();
            if (wasPressed) click?.Invoke();
        };

        element.LostMouseCapture += (_, _) =>
        {
            if (_pressed == element) _pressed = null;
        };
    }

    void DragOut(FrameworkElement source, string[] paths)
    {
        var existing = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (existing.Length == 0)
        {
            _shelf.Prune();
            return;
        }

        // Whatever the drop target chooses: a copy, a move (Explorer's default within
        // one drive) or a shortcut. Moved files drop off the shelf afterwards.
        var data = new DataObject(DataFormats.FileDrop, existing);
        DraggingOutChanged?.Invoke(true);
        try
        {
            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        catch
        {
            // The target app failed mid-drop; nothing to undo on our side.
        }
        finally
        {
            DraggingOutChanged?.Invoke(false);
            _shelf.Prune();
        }
    }

    static void Open(ShelfItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }
        catch
        {
            // Gone, or no app to open it with.
        }
    }

    void Clear_Click(object sender, RoutedEventArgs e) => _shelf.Clear();

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
