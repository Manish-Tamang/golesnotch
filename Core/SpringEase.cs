using System.Windows;
using System.Windows.Media.Animation;

namespace Notch.Core;

/// <summary>
/// A damped-spring curve, like iOS/macOS motion. Damping below 1 overshoots slightly
/// and settles; 1 is critically damped (no overshoot, fast settle).
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    public static readonly DependencyProperty DampingProperty = DependencyProperty.Register(
        nameof(Damping), typeof(double), typeof(SpringEase), new PropertyMetadata(0.8));

    public static readonly DependencyProperty FrequencyProperty = DependencyProperty.Register(
        nameof(Frequency), typeof(double), typeof(SpringEase), new PropertyMetadata(12.0));

    public SpringEase()
    {
        // EaseIn makes WPF use EaseInCore as-is; the spring shape itself is an ease-out.
        EasingMode = EasingMode.EaseIn;
    }

    public SpringEase(double damping, double frequency) : this()
    {
        Damping = damping;
        Frequency = frequency;
    }

    /// <summary>Damping ratio. 0.7 = gentle bounce, 1 = no bounce.</summary>
    public double Damping
    {
        get => (double)GetValue(DampingProperty);
        set => SetValue(DampingProperty, value);
    }

    /// <summary>Natural frequency over the animation's duration. Higher settles sooner.</summary>
    public double Frequency
    {
        get => (double)GetValue(FrequencyProperty);
        set => SetValue(FrequencyProperty, value);
    }

    protected override double EaseInCore(double t) => Spring(t) / Spring(1); // land exactly on 1

    double Spring(double t)
    {
        double z = Damping, w = Frequency;
        if (z < 1)
        {
            double wd = w * Math.Sqrt(1 - z * z);
            return 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + z * w / wd * Math.Sin(wd * t));
        }
        return 1 - Math.Exp(-w * t) * (1 + w * t);
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
