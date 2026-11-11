using Windows.System.Power;

namespace Notch.Services;

public sealed record PowerState(bool HasBattery, int Percent, bool IsCharging, bool IsPluggedIn);

/// <summary>Battery level and charger state. Events are raised on a background thread.</summary>
public sealed class PowerService
{
    const int LowBatteryPercent = 20;

    readonly object _gate = new();

    /// <summary>A desktop PC (no battery) reports a full, plugged-in state.</summary>
    public PowerState State { get; private set; } = new(HasBattery: false, Percent: 100, IsCharging: false, IsPluggedIn: true);

    public event Action? Changed;

    /// <summary>The charger was connected (true) or removed (false).</summary>
    public event Action<bool>? PluggedChanged;

    /// <summary>The battery just dropped to the low level while unplugged.</summary>
    public event Action? LowBattery;

    public void Start()
    {
        try
        {
            State = Read();
            PowerManager.RemainingChargePercentChanged += (_, _) => Refresh();
            PowerManager.BatteryStatusChanged += (_, _) => Refresh();
            PowerManager.PowerSupplyStatusChanged += (_, _) => Refresh();
        }
        catch
        {
            // No power API on this machine: the default state stays.
        }
    }

    void Refresh()
    {
        PowerState old, now;
        lock (_gate)
        {
            old = State;
            try
            {
                now = Read();
            }
            catch
            {
                return;
            }

            if (now == old) return;
            State = now;
        }

        Changed?.Invoke();

        if (now.HasBattery && now.IsPluggedIn != old.IsPluggedIn)
            PluggedChanged?.Invoke(now.IsPluggedIn);

        if (now.HasBattery && !now.IsPluggedIn
            && old.Percent > LowBatteryPercent && now.Percent <= LowBatteryPercent)
            LowBattery?.Invoke();
    }

    static PowerState Read()
    {
        var battery = PowerManager.BatteryStatus;
        return new PowerState(
            HasBattery: battery != BatteryStatus.NotPresent,
            Percent: PowerManager.RemainingChargePercent,
            IsCharging: battery == BatteryStatus.Charging,
            IsPluggedIn: PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent);
    }
}
