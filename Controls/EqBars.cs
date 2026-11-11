using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Notch.Controls;

/// <summary>Four little bars that bounce while music plays and rest when it doesn't.</summary>
public sealed class EqBars : StackPanel
{
    static readonly double[] Periods = { 0.42, 0.58, 0.36, 0.50 };
    const double RestScale = 0.32;

    readonly List<(ScaleTransform Scale, double Period)> _bars = new();
    bool _playing;

    public EqBars()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;

        foreach (double period in Periods)
        {
            var scale = new ScaleTransform(1, RestScale);
            Children.Add(new Rectangle
            {
                Width = 3,
                Height = 14,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(1, 0, 1, 0),
                Fill = Brushes.White,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = scale,
            });
            _bars.Add((scale, period));
        }
    }

    public bool IsPlaying
    {
        get => _playing;
        set
        {
            if (_playing == value) return;
            _playing = value;

            foreach (var (scale, period) in _bars)
            {
                DoubleAnimation animation = value
                    ? new DoubleAnimation(RestScale, 1, TimeSpan.FromSeconds(period))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    }
                    : new DoubleAnimation(RestScale, TimeSpan.FromMilliseconds(220));

                // 30 fps is plenty for this and keeps idle GPU use low.
                Timeline.SetDesiredFrameRate(animation, 30);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            }
        }
    }
}
