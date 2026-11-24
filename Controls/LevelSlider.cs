using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Notch.Core;

namespace Notch.Controls;

/// <summary>
/// A thin level bar for volume and brightness. Drag or click along it, or scroll over it,
/// to change the level; a knob appears while the pointer is on it.
/// </summary>
public sealed class LevelSlider : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(LevelSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackHeightProperty = DependencyProperty.Register(
        nameof(TrackHeight), typeof(double), typeof(LevelSlider),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DimmedProperty = DependencyProperty.Register(
        nameof(Dimmed), typeof(bool), typeof(LevelSlider),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    const double KnobSize = 14;
    const double WheelStep = 0.04;

    static readonly Brush TrackBrush = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    static readonly Brush DimFill = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    static readonly Pen KnobEdge = FrozenPen(Color.FromArgb(0x40, 0, 0, 0), 1);

    bool _dragging;

    public LevelSlider()
    {
        Height = 20;
        Cursor = Cursors.Hand;
        Focusable = false;
    }

    /// <summary>0 to 1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double TrackHeight
    {
        get => (double)GetValue(TrackHeightProperty);
        set => SetValue(TrackHeightProperty, value);
    }

    /// <summary>Drawn faded, as for a muted volume.</summary>
    public bool Dimmed
    {
        get => (bool)GetValue(DimmedProperty);
        set => SetValue(DimmedProperty, value);
    }

    /// <summary>The user moved the slider. Not raised when <see cref="Value"/> is set in code.</summary>
    public event Action<double>? ValueChanging;

    /// <summary>Animates to a level set from outside, unless the user is dragging.</summary>
    public void Show(double value, bool animate)
    {
        if (_dragging) return;
        if (animate) Motion.To(this, ValueProperty, Math.Clamp(value, 0, 1), 200);
        else Motion.Set(this, ValueProperty, Math.Clamp(value, 0, 1));
    }

    /// <summary>Moves the level by a step, as if scrolled.</summary>
    public void Nudge(double step) => Change(Math.Clamp(Value + step, 0, 1));

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0) return;

        // The whole height takes clicks, not just the thin bar.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        double track = TrackHeight;
        double y = (height - track) / 2;
        double value = Math.Clamp(Value, 0, 1);
        drawingContext.DrawRoundedRectangle(TrackBrush, null, new Rect(0, y, width, track), track / 2, track / 2);
        if (value > 0)
        {
            double fill = Math.Max(track, width * value);
            drawingContext.DrawRoundedRectangle(Dimmed ? DimFill : Brushes.White, null,
                                                new Rect(0, y, fill, track), track / 2, track / 2);
        }

        if (IsMouseOver || _dragging)
        {
            double x = Math.Clamp(width * value, KnobSize / 2, width - KnobSize / 2);
            drawingContext.DrawEllipse(Brushes.White, KnobEdge, new Point(x, height / 2), KnobSize / 2, KnobSize / 2);
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeave(MouseEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        _dragging = true;
        CaptureMouse();
        SetFromPointer(e.GetPosition(this).X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromPointer(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        _dragging = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragging = false;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;
        Nudge(Math.Sign(e.Delta) * WheelStep);
    }

    void SetFromPointer(double x)
    {
        if (ActualWidth <= 0) return;
        Change(Math.Clamp(x / ActualWidth, 0, 1));
    }

    void Change(double value)
    {
        Motion.Set(this, ValueProperty, value);
        ValueChanging?.Invoke(value);
    }

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
