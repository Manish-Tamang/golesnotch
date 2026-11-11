using System.Windows;
using System.Windows.Media;
using Notch.Core;

namespace Notch.Controls;

/// <summary>
/// The black body of the notch: a rounded rectangle, centred at the top, whose width,
/// height and corner radius each ride on a <see cref="Spring"/>.
/// </summary>
/// <remarks>
/// Only the geometry changes from frame to frame, which touches the renderer and nothing
/// else. Animating an element's Width and Height instead would re-run layout for
/// everything inside it on every frame, which is what makes resize animations stutter.
/// </remarks>
public sealed class IslandShape : FrameworkElement
{
    // Classic style: the rectangle starts above the screen so its top corners are cut
    // off, and two small "shoulders" curve outwards where it meets the top edge.
    const double ClassicOverhang = 4;
    const double ShoulderSize = 9;
    const double ShoulderOverlap = 2; // tucked under the body so no seam shows between them

    // A 9px concave corner, plus the 2px strip that overlaps the body.
    static readonly Geometry LeftShoulder = Frozen("M0,0 L11,0 L11,9 L9,9 A9,9 0 0 0 0,0 Z");
    static readonly Geometry RightShoulder = Frozen("M11,0 L0,0 L0,9 L2,9 A9,9 0 0 1 11,0 Z");

    const byte GlowAlpha = 0xB4;

    readonly Spring _width = new(0);
    readonly Spring _height = new(0);
    readonly Spring _radius = new(0);

    readonly TranslateTransform _leftShoulderShift = new();
    readonly TranslateTransform _rightShoulderShift = new();

    readonly GradientStop _glowTop = new(Colors.Transparent, 0.38);
    readonly GradientStop _glowBottom = new(Colors.Transparent, 1);
    readonly LinearGradientBrush _glow;
    Color? _glowColor;

    bool _classic;
    bool _running;
    TimeSpan? _lastFrame;

    public IslandShape()
    {
        _glow = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            Opacity = 0,
        };
        _glow.GradientStops.Add(_glowTop);
        _glow.GradientStops.Add(_glowBottom);

        SizeChanged += (_, _) => Apply();
    }

    /// <summary>The body's outline. Also used to clip the notch's content.</summary>
    public RectangleGeometry Outline { get; } = new();

    /// <summary>The visible height right now, mid-animation included.</summary>
    public double CurrentHeight => _height.Value;

    /// <summary>Raised on every frame the island moves, so others can follow its edges.</summary>
    public event Action? Moved;

    /// <summary>Sends the island towards a new size.</summary>
    public void MoveTo(double width, double height, double radius, bool classic, bool animate)
    {
        if (classic != _classic)
        {
            _classic = classic;
            InvalidateVisual(); // the shoulders appear or disappear
        }

        if (!animate)
        {
            _width.Snap(width);
            _height.Snap(height);
            _radius.Snap(radius);
            Apply();
            return;
        }

        // Growing gets a soft overshoot; shrinking settles without one.
        bool growing = width * height >= _width.Target * _height.Target;
        double response = (growing ? 0.5 : 0.42) * Motion.SpeedFactor;
        double damping = growing ? 0.78 : 0.95;

        _width.To(width, response, damping);
        _height.To(height, response, damping);
        _radius.To(radius, response, 1);

        if (!_running)
        {
            _running = true;
            _lastFrame = null;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    /// <summary>
    /// Tints the lower part of the island with a colour (taken from the album cover).
    /// Null fades the tint out.
    /// </summary>
    public void SetGlow(Color? color)
    {
        if (color == _glowColor) return;
        bool wasOn = _glowColor != null;
        _glowColor = color;

        if (color is not { } c)
        {
            Motion.To(_glow, Brush.OpacityProperty, 0, 450);
            return;
        }

        // Fade between the same hue at zero and partial opacity, so the gradient never
        // passes through grey.
        var top = Color.FromArgb(0, c.R, c.G, c.B);
        var bottom = Color.FromArgb(GlowAlpha, c.R, c.G, c.B);

        if (wasOn)
        {
            Motion.To(_glowTop, GradientStop.ColorProperty, top, 700);
            Motion.To(_glowBottom, GradientStop.ColorProperty, bottom, 700);
        }
        else
        {
            Motion.Set(_glowTop, GradientStop.ColorProperty, top);
            Motion.Set(_glowBottom, GradientStop.ColorProperty, bottom);
        }

        Motion.To(_glow, Brush.OpacityProperty, 1, 600);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawGeometry(Brushes.Black, null, Outline);

        if (_classic)
        {
            drawingContext.PushTransform(_leftShoulderShift);
            drawingContext.DrawGeometry(Brushes.Black, null, LeftShoulder);
            drawingContext.Pop();

            drawingContext.PushTransform(_rightShoulderShift);
            drawingContext.DrawGeometry(Brushes.Black, null, RightShoulder);
            drawingContext.Pop();
        }

        drawingContext.DrawGeometry(_glow, null, Outline);
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double seconds = _lastFrame is { } last ? (now - last).TotalSeconds : 1.0 / 120;
        if (_lastFrame != null && seconds <= 0) return; // WPF can raise this twice for one frame
        _lastFrame = now;

        seconds = Math.Min(seconds, 0.05); // don't leap after a stall
        _width.Step(seconds);
        _height.Step(seconds);
        _radius.Step(seconds);
        Apply();

        if (_width.IsAtRest && _height.IsAtRest && _radius.IsAtRest)
        {
            CompositionTarget.Rendering -= OnFrame;
            _running = false;
        }
    }

    /// <summary>Writes the springs' current values into the geometry.</summary>
    void Apply()
    {
        double width = Math.Max(0, _width.Value);
        double height = Math.Max(0, _height.Value);
        double radius = Math.Max(0, _radius.Value);

        double top = _classic ? -(radius + ClassicOverhang) : 0;
        double left = (ActualWidth - width) / 2;

        Outline.Rect = new Rect(left, top, width, height - top);
        Outline.RadiusX = radius;
        Outline.RadiusY = radius;

        _leftShoulderShift.X = left - ShoulderSize;
        _rightShoulderShift.X = left + width - ShoulderOverlap;

        Moved?.Invoke();
    }

    static Geometry Frozen(string path)
    {
        var geometry = Geometry.Parse(path);
        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }
}
