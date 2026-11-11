using System.Diagnostics;
using System.IO;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Notch.Services;

public enum DeviceKind
{
    /// <summary>A USB stick or card with a drive letter.</summary>
    Drive,
    /// <summary>A phone, camera or player that shows up under This PC without a drive letter.</summary>
    Portable,
    Bluetooth,
}

/// <param name="DrivePath">The drive's root, such as E:\. Only set for <see cref="DeviceKind.Drive"/>.</param>
public sealed record ConnectedDevice(string Id, string Name, DeviceKind Kind, string? DrivePath);

/// <summary>
/// Watches for storage devices and Bluetooth devices being connected and removed.
/// It stands in for what the taskbar tray normally offers: a notice that the device
/// arrived, a way to open it, and a way to eject it.
/// </summary>
/// <remarks>Events are raised on background threads.</remarks>
public sealed class DeviceService
{
    static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(3);

    readonly object _gate = new();
    readonly Dictionary<string, ConnectedDevice> _devices = new();
    readonly List<DeviceWatcher> _watchers = new(); // kept alive for as long as the app runs
    string _lastAnnounced = "";
    DateTime _lastAnnouncedAt;

    /// <summary>What is connected right now, by name.</summary>
    public IReadOnlyList<ConnectedDevice> Devices { get; private set; } = Array.Empty<ConnectedDevice>();

    public event Action? Changed;
    public event Action<ConnectedDevice>? Connected;
    public event Action<ConnectedDevice>? Disconnected;

    public void Start()
    {
        TryWatch(() => DeviceInformation.CreateWatcher(DeviceClass.PortableStorageDevice), DeviceKind.Portable);

        // Headphones and the like can appear as classic Bluetooth, low energy, or both.
        var connected = BluetoothConnectionStatus.Connected;
        TryWatch(() => DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(connected)), DeviceKind.Bluetooth);
        TryWatch(() => DeviceInformation.CreateWatcher(
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(connected)), DeviceKind.Bluetooth);
    }

    /// <summary>Opens the device: the drive's folder, This PC for a phone, Bluetooth settings otherwise.</summary>
    public static void Open(ConnectedDevice device)
    {
        try
        {
            var start = device.Kind switch
            {
                DeviceKind.Drive when device.DrivePath != null => new ProcessStartInfo(device.DrivePath),
                DeviceKind.Portable => new ProcessStartInfo("explorer.exe", "shell:MyComputerFolder"),
                _ => new ProcessStartInfo("ms-settings:bluetooth"),
            };
            start.UseShellExecute = true;
            Process.Start(start);
        }
        catch
        {
            // Nothing to open it with.
        }
    }

    /// <summary>
    /// Safely removes a drive, the same as "Eject" in File Explorer. Call from the UI thread.
    /// </summary>
    /// <returns>False when the device isn't a drive or Windows refused.</returns>
    public static bool Eject(ConnectedDevice device)
    {
        if (device.DrivePath == null) return false;

        try
        {
            // The Windows shell exposes Explorer's own Eject command through COM.
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return false;

            const int ThisPC = 17;
            dynamic shell = Activator.CreateInstance(shellType)!;
            shell.NameSpace(ThisPC).ParseName(device.DrivePath).InvokeVerb("Eject");
            return true;
        }
        catch
        {
            return false;
        }
    }

    void TryWatch(Func<DeviceWatcher> create, DeviceKind kind)
    {
        try
        {
            var watcher = create();

            // A watcher first lists what is already connected. Only what arrives after
            // that is news.
            bool listed = false;
            watcher.Added += (_, info) => OnAdded(info, kind, announce: listed);
            watcher.Removed += (_, update) => OnRemoved(update.Id);
            watcher.Updated += (_, _) => { }; // Windows only reports removals if this is handled too
            watcher.EnumerationCompleted += (_, _) => listed = true;

            _watchers.Add(watcher);
            watcher.Start();
        }
        catch
        {
            // This kind of device can't be watched on this PC (no Bluetooth radio, say).
        }
    }

    void OnAdded(DeviceInformation info, DeviceKind kind, bool announce)
    {
        string name = info.Name ?? "";
        if (name.Length == 0) return;

        string? drive = kind == DeviceKind.Portable ? DrivePathOf(info.Id) : null;
        if (drive != null)
        {
            kind = DeviceKind.Drive;
            name = DriveName(drive);
        }

        var device = new ConnectedDevice(info.Id, name, kind, drive);
        bool isNew;
        lock (_gate)
        {
            isNew = !_devices.ContainsKey(info.Id);
            _devices[info.Id] = device;
            Publish();
        }

        Changed?.Invoke();
        if (announce && isNew && IsNews(name)) Connected?.Invoke(device);
    }

    void OnRemoved(string id)
    {
        ConnectedDevice? device;
        lock (_gate)
        {
            if (!_devices.Remove(id, out device)) return;
            Publish();
        }

        Changed?.Invoke();
        if (IsNews(device.Name)) Disconnected?.Invoke(device);
    }

    /// <summary>Call with the lock held.</summary>
    void Publish()
    {
        // One device can be listed twice (classic and low-energy Bluetooth); show it once.
        Devices = _devices.Values
            .GroupBy(device => (device.Kind, device.Name))
            .Select(group => group.First())
            .OrderBy(device => device.Name)
            .ToList();
    }

    /// <summary>False when the same device was announced a moment ago.</summary>
    bool IsNews(string name)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (name == _lastAnnounced && now - _lastAnnouncedAt < RepeatWindow) return false;
            _lastAnnounced = name;
            _lastAnnouncedAt = now;
            return true;
        }
    }

    /// <summary>The drive root (E:\) of a storage device, or null if it has no drive letter.</summary>
    static string? DrivePathOf(string deviceId)
    {
        try
        {
            string path = Windows.Devices.Portable.StorageDevice.FromId(deviceId).Path;
            return path.Length is 2 or 3 && path[1] == ':' ? path[..2] + "\\" : null;
        }
        catch
        {
            return null; // a phone or camera, or the drive isn't ready yet
        }
    }

    static string DriveName(string drivePath)
    {
        string letter = drivePath[..2];
        try
        {
            string label = new DriveInfo(drivePath).VolumeLabel;
            if (!string.IsNullOrWhiteSpace(label)) return $"{label} ({letter})";
        }
        catch
        {
            // Not ready to be read yet.
        }
        return $"USB drive ({letter})";
    }
}
