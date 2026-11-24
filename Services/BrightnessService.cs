using System.Management;
using Notch.Interop;

namespace Notch.Services;

/// <summary>
/// Screen brightness of the built-in display through WMI, or of the primary external
/// monitor over DDC/CI. Only the built-in display reports changes made elsewhere,
/// such as with the brightness keys.
/// </summary>
/// <remarks>Events are raised on background threads.</remarks>
public sealed class BrightnessService
{
    const string Namespace = @"root\WMI";

    static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(15);

    readonly object _gate = new();
    ManagementEventWatcher? _watcher;
    Timer? _retry;
    bool _wmi;
    IntPtr _monitor;
    double? _pending;
    bool _applying;
    DateTime _lastLocalChange;

    /// <summary>0 to 1.</summary>
    public double Level { get; private set; } = 1;

    /// <summary>False when neither the built-in display nor the monitor can be controlled.</summary>
    public bool IsSupported { get; private set; }

    public event Action? Changed;

    /// <summary>Somebody changed the brightness: the keys, Windows, or the notch itself.</summary>
    public event Action? Adjusted;

    /// <summary>
    /// Looks for a screen it can control. If there isn't one yet (a driver still loading, or
    /// the display device disabled), it keeps looking, so brightness appears without a restart.
    /// </summary>
    public void Start() => _retry = new Timer(_ => Initialise(), null, TimeSpan.Zero, RetryEvery);

    public void Set(double level)
    {
        level = Math.Clamp(level, 0, 1);
        lock (_gate)
        {
            if (!IsSupported) return;
            Level = level;
            _pending = level;
            _lastLocalChange = DateTime.UtcNow;
            if (_applying) return;
            _applying = true;
        }

        Changed?.Invoke();
        Task.Run(ApplyPending);
    }

    void Initialise()
    {
        if (IsSupported)
        {
            _retry?.Dispose();
            return;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(Namespace, "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
            foreach (ManagementObject display in searcher.Get())
            {
                using (display)
                {
                    Level = Convert.ToDouble(display["CurrentBrightness"]) / 100;
                    _wmi = true;
                }
                break;
            }
        }
        catch
        {
        }

        if (_wmi)
        {
            IsSupported = true;
            try
            {
                _watcher = new ManagementEventWatcher(new ManagementScope(Namespace),
                                                      new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                _watcher.EventArrived += (_, e) => OnReported(Convert.ToDouble(e.NewEvent["Brightness"]) / 100);
                _watcher.Start();
            }
            catch
            {
            }
        }
        else
        {
            if (_monitor == IntPtr.Zero) _monitor = MonitorBrightness.OpenPrimary();
            if (_monitor != IntPtr.Zero && MonitorBrightness.Read(_monitor) is { } level)
            {
                Level = level;
                IsSupported = true;
            }
        }

        if (IsSupported)
        {
            _retry?.Dispose();
            Changed?.Invoke();
        }
    }

    void OnReported(double level)
    {
        lock (_gate)
        {
            // Our own changes come back as events too, lagging behind a drag.
            if (_applying || DateTime.UtcNow - _lastLocalChange < TimeSpan.FromMilliseconds(600)) return;
            if (Math.Abs(level - Level) < 0.005) return;
            Level = level;
        }

        Changed?.Invoke();
        Adjusted?.Invoke();
    }

    void ApplyPending()
    {
        while (true)
        {
            double level;
            lock (_gate)
            {
                if (_pending is not { } next)
                {
                    _applying = false;
                    return;
                }
                level = next;
                _pending = null;
            }

            try
            {
                if (_wmi) SetWmi(level);
                else if (_monitor != IntPtr.Zero) MonitorBrightness.Write(_monitor, level);
            }
            catch
            {
            }
        }
    }

    static void SetWmi(double level)
    {
        using var methods = new ManagementClass(Namespace, "WmiMonitorBrightnessMethods", null);
        foreach (ManagementObject display in methods.GetInstances())
        {
            using (display)
                display.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)Math.Round(level * 100) });
            break;
        }
    }
}
