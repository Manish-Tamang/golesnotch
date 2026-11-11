namespace Notch.Core;

/// <summary>
/// A number on a damped spring. Unlike a timed animation it has a velocity, and keeps
/// it when the target changes mid-flight. That is what makes motion feel continuous
/// when the notch is opened and closed in quick succession: nothing ever restarts
/// from a standstill.
/// </summary>
public sealed class Spring
{
    // Close enough to the target, and slow enough, to stop.
    const double RestDistance = 0.01;
    const double RestSpeed = 0.05;

    // Small fixed sub-steps keep the simulation stable and identical at any frame rate.
    const double MaxStep = 0.004;

    double _stiffness;
    double _damping;

    public Spring(double value)
    {
        Value = value;
        Target = value;
    }

    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; private set; }

    public bool IsAtRest => Value == Target && Velocity == 0;

    /// <summary>Jumps to a value without animating.</summary>
    public void Snap(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
    }

    /// <summary>Heads for a new target, keeping the current velocity.</summary>
    /// <param name="response">Roughly how long the move takes, in seconds.</param>
    /// <param name="dampingRatio">1 arrives without overshoot; lower values bounce a little.</param>
    public void To(double target, double response, double dampingRatio)
    {
        Target = target;
        double frequency = 2 * Math.PI / Math.Max(0.05, response);
        _stiffness = frequency * frequency;
        _damping = 2 * dampingRatio * frequency;
    }

    /// <summary>Advances the simulation by a frame's worth of time.</summary>
    public void Step(double seconds)
    {
        if (IsAtRest) return;

        while (seconds > 0)
        {
            double dt = Math.Min(seconds, MaxStep);
            double acceleration = _stiffness * (Target - Value) - _damping * Velocity;
            Velocity += acceleration * dt;
            Value += Velocity * dt;
            seconds -= dt;
        }

        if (Math.Abs(Target - Value) < RestDistance && Math.Abs(Velocity) < RestSpeed)
        {
            Value = Target;
            Velocity = 0;
        }
    }
}
