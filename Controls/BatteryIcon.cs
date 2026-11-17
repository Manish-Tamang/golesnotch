using System.Windows;
using System.Windows.Media;
using Notch.Core;

namespace Notch.Controls;

/// <summary>
/// A small battery: an outline that fills with the charge level. While the charger is
/// connected the fill turns the accent colour and a bolt sits on top, so it's clear at a
/// glance that it's charging, long after the charger popup has gone.
/// </summary>
public sealed class BatteryIcon : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(BatteryIcon),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PluggedInProperty = DependencyProperty.Register(
        nameof(PluggedIn), typeof(bool), typeof(BatteryIcon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(BatteryIcon),
        new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    const double LowLevel = 0.2;

    static readonly Brush OutlineBrush = Frozen(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF));
    static readonly Pen OutlinePen = FrozenPen(OutlineBrush, 1);

    // The same bolt as the "BoltGlyph" resource, 13 × 16.
    static readonly Geometry Bolt = FrozenGeometry("M9,0 L1,9 H6.5 L5,16 L13,6.5 H7.5 Z");

    public BatteryIcon()
    {
        Width = 25;
        Height = 12;
        IsHitTestVisible = false;
        SetResourceReference(AccentProperty, Theme.AccentKey);
    }

    /// <summary>Charge, 0 to 1.</summary>
    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>The charger is connected (charging, or full and holding).</summary>
    public bool PluggedIn
    {
        get => (bool)GetValue(PluggedInProperty);
        set => SetValue(PluggedInProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width < 8 || height < 6) return;

        // Body, with the terminal nub on the right.
        const double NubWidth = 2;
        var body = new Rect(0.5, 0.5, width - NubWidth - 1.5, height - 1);
        double bodyRadius = Math.Min(3.5, body.Height / 3);
        drawingContext.DrawRoundedRectangle(null, OutlinePen, body, bodyRadius, bodyRadius);

        double nubHeight = Math.Round(height * 0.36);
        drawingContext.DrawRoundedRectangle(OutlineBrush, null,
            new Rect(body.Right + 0.5, (height - nubHeight) / 2, NubWidth, nubHeight), 1, 1);

        // Fill, inset from the outline.
        double level = double.IsNaN(Level) ? 0 : Math.Clamp(Level, 0, 1);
        var inner = body;
        inner.Inflate(-2, -2);
        if (inner.Width > 0 && inner.Height > 0 && level > 0)
        {
            var fill = new Rect(inner.X, inner.Y, Math.Max(inner.Height * 0.6, inner.Width * level), inner.Height);
            Brush brush = PluggedIn ? Accent : level <= LowLevel ? Theme.Warning : Brushes.White;
            double fillRadius = Math.Max(0, bodyRadius - 1.6);
            drawingContext.DrawRoundedRectangle(brush, null, fill, fillRadius, fillRadius);
        }

        if (!PluggedIn) return;

        // The bolt, centred on the body, with a dark edge so it reads over any fill.
        double scale = body.Height * 0.95 / 16;
        double x = body.X + (body.Width - 13 * scale) / 2;
        double y = body.Y + (body.Height - 16 * scale) / 2;
        drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale, x, y));
        drawingContext.DrawGeometry(Brushes.White, FrozenPen(Brushes.Black, 2.2 / scale), Bolt);
        drawingContext.DrawGeometry(Brushes.White, null, Bolt);
        drawingContext.Pop();
    }

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    static Geometry FrozenGeometry(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }
}
