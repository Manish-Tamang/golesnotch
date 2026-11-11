using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Notch.Core;

/// <summary>
/// The one place timed animations are started, so every view shares the same curves and
/// the "animation speed" setting applies everywhere. (The island itself moves on a
/// physical <see cref="Spring"/>, which reads <see cref="SpeedFactor"/> too.)
/// </summary>
public static class Motion
{
    /// <summary>Multiplies every duration. Above 1 is slower, below 1 is faster.</summary>
    public static double SpeedFactor { get; set; } = 1;

    /// <summary>Soft bounce, for things that open or grow.</summary>
    public static IEasingFunction Spring() => new SpringEase(0.8, 12);

    /// <summary>No bounce, for things that close or shrink.</summary>
    public static IEasingFunction Settle() => new SpringEase(1.0, 10);

    public static IEasingFunction EaseOut() => new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Stops any running animation on the property and sets it directly.</summary>
    public static void Set(IAnimatable target, DependencyProperty property, double value)
    {
        target.BeginAnimation(property, null);
        ((DependencyObject)target).SetValue(property, value);
    }

    /// <summary>Stops any running animation on the property and sets it directly.</summary>
    public static void Set(IAnimatable target, DependencyProperty property, Color value)
    {
        target.BeginAnimation(property, null);
        ((DependencyObject)target).SetValue(property, value);
    }

    /// <summary>
    /// Animates from wherever the property is now, so an animation that interrupts
    /// another one carries on smoothly instead of jumping.
    /// </summary>
    public static void To(IAnimatable target, DependencyProperty property, double to, double ms,
                          IEasingFunction? ease = null, double delayMs = 0, Action? completed = null)
    {
        var animation = new DoubleAnimation(to, Time(ms)) { EasingFunction = ease ?? EaseOut() };
        if (delayMs > 0) animation.BeginTime = Time(delayMs);
        if (completed != null) animation.Completed += (_, _) => completed();
        target.BeginAnimation(property, animation);
    }

    /// <summary>The same, for a colour.</summary>
    public static void To(IAnimatable target, DependencyProperty property, Color to, double ms)
    {
        target.BeginAnimation(property, new ColorAnimation(to, Time(ms)) { EasingFunction = EaseOut() });
    }

    static TimeSpan Time(double ms) => TimeSpan.FromMilliseconds(ms * SpeedFactor);
}
