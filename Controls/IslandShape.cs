using System.Windows;
using System.Windows.Media;
using Notch.Core;

namespace Notch.Controls;

/// <summary>How the island meets the top of the screen.</summary>
public enum IslandEdge
{
    /// <summary>Hangs below the top edge, rounded on all four corners.</summary>
    Floating,
    /// <summary>Joined to the top edge, with curved shoulders.</summary>
    Classic,
    /// <summary>Asleep: a thin strip pulled up against the top edge, whatever the style.</summary>
    Tucked,
}

/// <summary>
/// The black body of the notch: a rounded rectangle, centred at the top, whose width,
/// top and bottom edges and corner radius each ride on a <see cref="Spring"/>.
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
    readonly Spring _height = new(0);   // the bottom edge, measured from the island's top
    readonly Spring _top = new(0);      // the top edge; above zero when joined to the screen edge
    readonly Spring _radius = new(0);
    double _targetArea;

    readonly TranslateTransform _leftShoulderShift = new();
    readonly TranslateTransform _rightShoulderShift = new();

    readonly GradientStop _glowTop = new(Colors.Transparent, 0.38);
    readonly GradientStop _glowBottom = new(Colors.Transparent, 1);
    readonly LinearGradientBrush _glow;
    Color? _glowColor;

    // Shoulders fade rather than pop when the island joins or leaves the top edge.
    readonly SolidColorBrush _shoulderBrush = new(Colors.Black) { Opacity = 0 };

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
    /// <param name="height">Visible height, from the island's top (or the screen's top when tucked).</param>
    /// <param name="gap">How far the island's own top sits below the screen's top edge.</param>
    public void MoveTo(double width, double height, double radius, IslandEdge edge, double gap, bool animate)
    {
        bool classic = edge == IslandEdge.Classic;
        if (classic != _classic)
        {
            _classic = classic;
            if (animate)
                Motion.To(_shoulderBrush, Brush.OpacityProperty, classic ? 1 : 0, classic ? 260 : 120);
            else
                Motion.Set(_shoulderBrush, Brush.OpacityProperty, classic ? 1 : 0);
        }

        // Joined to the screen edge, the rectangle starts above it so its top corners are cut off.
        double top = edge switch
        {
            IslandEdge.Classic => -(radius + ClassicOverhang),
            IslandEdge.Tucked => -(gap + radius + ClassicOverhang),
            _ => 0,
        };
        double bottom = edge == IslandEdge.Tucked ? height - gap : height;

        if (!animate)
        {
            _width.Snap(width);
            _height.Snap(bottom);
            _top.Snap(top);
            _radius.Snap(radius);
            _targetArea = width * height;
            Apply();
            return;
        }

        // Growing gets a soft overshoot; shrinking settles without one.
        bool growing = width * height >= _targetArea;
        _targetArea = width * height;
        double response = (growing ? 0.5 : 0.42) * Motion.SpeedFactor;
        double damping = growing ? 0.78 : 0.95;

        _width.To(width, response, damping);
        _height.To(bottom, response, damping);
        // The top shares the radius's spring exactly, so in classic style it stays
        // locked to the radius and the cut-off corners never peek below the screen edge.
        _top.To(top, response, 1);
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

        // Always drawn; the brush's opacity shows or hides them.
        drawingContext.PushTransform(_leftShoulderShift);
        drawingContext.DrawGeometry(_shoulderBrush, null, LeftShoulder);
        drawingContext.Pop();

        drawingContext.PushTransform(_rightShoulderShift);
        drawingContext.DrawGeometry(_shoulderBrush, null, RightShoulder);
        drawingContext.Pop();

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
        _top.Step(seconds);
        _radius.Step(seconds);
        Apply();

        if (_width.IsAtRest && _height.IsAtRest && _top.IsAtRest && _radius.IsAtRest)
        {
            CompositionTarget.Rendering -= OnFrame;
            _running = false;
        }
    }

    /// <summary>Writes the springs' current values into the geometry.</summary>
    void Apply()
    {
        double width = Math.Max(0, _width.Value);
        double radius = Math.Max(0, _radius.Value);
        double top = _top.Value;
        double bottom = Math.Max(top, _height.Value);
        double left = (ActualWidth - width) / 2;

        Outline.Rect = new Rect(left, top, width, bottom - top);
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
