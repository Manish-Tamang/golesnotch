using System.Windows.Threading;

namespace Notch.Services;

/// <summary>A simple countdown. Create it on the UI thread; events are raised there.</summary>
public sealed class FocusTimer
{
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    DateTime _endsAt;

    public FocusTimer()
    {
        Remaining = Duration;
        _tick.Tick += (_, _) => OnTick();
    }

    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(25);
    public TimeSpan Remaining { get; private set; }
    public bool IsRunning { get; private set; }

    /// <summary>Paused part-way through a session.</summary>
    public bool IsPaused => !IsRunning && Remaining < Duration;

    /// <summary>The time left or the running state changed.</summary>
    public event Action? Changed;

    /// <summary>The countdown reached zero.</summary>
    public event Action? Finished;

    /// <summary>Picks a new session length and resets the countdown.</summary>
    public void SetDuration(TimeSpan duration)
    {
        Stop();
        Duration = duration;
        Remaining = duration;
        Changed?.Invoke();
    }

    public void Start()
    {
        if (IsRunning) return;
        _endsAt = DateTime.UtcNow + Remaining;
        IsRunning = true;
        _tick.Start();
        Changed?.Invoke();
    }

    public void Pause()
    {
        if (!IsRunning) return;
        Remaining = _endsAt - DateTime.UtcNow;
        Stop();
        Changed?.Invoke();
    }

    public void Reset()
    {
        Stop();
        Remaining = Duration;
        Changed?.Invoke();
    }

    void Stop()
    {
        IsRunning = false;
        _tick.Stop();
    }

    void OnTick()
    {
        Remaining = _endsAt - DateTime.UtcNow;
        if (Remaining > TimeSpan.Zero)
        {
            Changed?.Invoke();
            return;
        }

        Stop();
        Remaining = Duration;
        Changed?.Invoke();
        Finished?.Invoke();
    }
}
