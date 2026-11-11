using System.Windows;
using System.Windows.Media;
using Notch.Core;

namespace Notch.Controls;

/// <summary>A ring that fills clockwise from 12 o'clock. Used for battery and timer progress.</summary>
public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
        nameof(StrokeWidth), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    static readonly Brush Track = CreateTrack();

    public ProgressRing()
    {
        IsHitTestVisible = false;
        SetWarning(false);
    }

    /// <summary>Filled fraction, 0 to 1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    /// <summary>Red when true; otherwise the ring follows the accent colour.</summary>
    public void SetWarning(bool warning)
    {
        if (warning)
            Stroke = Theme.Warning;
        else
            SetResourceReference(StrokeProperty, Theme.AccentKey);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = StrokeWidth;
        double radius = (Math.Min(ActualWidth, ActualHeight) - width) / 2;
        if (radius <= 0) return;

        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        drawingContext.DrawEllipse(null, new Pen(Track, width), centre, radius, radius);

        double value = double.IsNaN(Value) ? 0 : Math.Clamp(Value, 0, 1);
        if (value <= 0.001) return;

        var pen = new Pen(Stroke, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (value >= 0.999)
        {
            drawingContext.DrawEllipse(null, pen, centre, radius, radius);
            return;
        }

        double angle = value * 360;
        double radians = (angle - 90) * Math.PI / 180;
        var start = new Point(centre.X, centre.Y - radius);
        var end = new Point(centre.X + radius * Math.Cos(radians), centre.Y + radius * Math.Sin(radians));

        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(end, new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise,
                          isStroked: true, isSmoothJoin: false);
        }
        arc.Freeze();
        drawingContext.DrawGeometry(null, pen, arc);
    }

    static Brush CreateTrack()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        brush.Freeze();
        return brush;
    }
}
