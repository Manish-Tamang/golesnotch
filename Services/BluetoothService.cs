using Notch.Interop;
using Windows.Devices.Enumeration;

namespace Notch.Services;

public enum BluetoothKind { Audio, Input, Phone, Computer, Other }

public sealed record BluetoothDeviceItem(
    string Id, string Name, BluetoothKind Kind, bool IsConnected, bool HasAudio, Guid? Container);

public enum PairOutcome { Paired, Failed, NeedsSettings, Cancelled }

/// <summary>
/// Paired Bluetooth devices, nearby devices to pair with, and the actions on them:
/// pair, remove, and connect or disconnect headphones and speakers.
/// </summary>
/// <remarks><see cref="Changed"/> is raised on background threads.</remarks>
public sealed class BluetoothService
{
    const string Classic = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    const string LowEnergy = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    const string ContainerKey = "System.Devices.Aep.ContainerId";
    const string MajorClassKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    const string AppearanceKey = "System.Devices.Aep.Bluetooth.Le.Appearance.Category";

    static readonly string[] Properties = { IsConnectedKey, ContainerKey, MajorClassKey, AppearanceKey };

    readonly object _gate = new();
    readonly Dictionary<string, DeviceInformation> _paired = new();
    readonly Dictionary<string, DeviceInformation> _nearby = new();
    readonly List<DeviceWatcher> _pairedWatchers = new();
    readonly List<DeviceWatcher> _scanWatchers = new();
    Dictionary<Guid, bool> _audio = new();
    Timer? _audioRefresh;

    public IReadOnlyList<BluetoothDeviceItem> Paired { get; private set; } = Array.Empty<BluetoothDeviceItem>();
    public IReadOnlyList<BluetoothDeviceItem> Nearby { get; private set; } = Array.Empty<BluetoothDeviceItem>();
    public bool IsScanning { get; private set; }

    public event Action? Changed;

    public void Start()
    {
        foreach (string protocol in new[] { Classic, LowEnergy })
        {
            var watcher = Watch(Selector(protocol, paired: true), _paired);
            if (watcher != null) _pairedWatchers.Add(watcher);
        }
        ScheduleAudioRefresh();
    }

    public void StartScan()
    {
        lock (_gate)
        {
            if (IsScanning) return;
            IsScanning = true;
            _nearby.Clear();
        }

        var classic = Watch(Selector(Classic, paired: false), _nearby);
        var le = Watch(Selector(LowEnergy, paired: false)
                       + " AND System.Devices.Aep.Bluetooth.Le.IsConnectable:=System.StructuredQueryType.Boolean#True", _nearby);
        lock (_gate)
        {
            if (classic != null) _scanWatchers.Add(classic);
            if (le != null) _scanWatchers.Add(le);
            Publish();
        }
        Changed?.Invoke();
    }

    public void StopScan()
    {
        List<DeviceWatcher> watchers;
        lock (_gate)
        {
            if (!IsScanning) return;
            IsScanning = false;
            watchers = _scanWatchers.ToList();
            _scanWatchers.Clear();
            _nearby.Clear();
            Publish();
        }

        foreach (var watcher in watchers)
        {
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    watcher.Stop();
            }
            catch
            {
            }
        }
        Changed?.Invoke();
    }

    /// <param name="confirmCode">Asked when the device shows a code to compare. Returns whether the codes match.</param>
    /// <param name="showCode">Called when the code has to be typed on the device itself, such as a keyboard.</param>
    public async Task<PairOutcome> PairAsync(string id, Func<string, Task<bool>> confirmCode, Action<string> showCode)
    {
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(id);
            var custom = info.Pairing.Custom;
            bool cancelled = false;

            custom.PairingRequested += async (_, args) =>
            {
                switch (args.PairingKind)
                {
                    case DevicePairingKinds.ConfirmOnly:
                        args.Accept();
                        break;
                    case DevicePairingKinds.DisplayPin:
                        showCode(args.Pin);
                        args.Accept();
                        break;
                    case DevicePairingKinds.ConfirmPinMatch:
                        var deferral = args.GetDeferral();
                        if (await confirmCode(args.Pin)) args.Accept();
                        else cancelled = true;
                        deferral.Complete();
                        break;
                }
            };

            var kinds = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ConfirmPinMatch;
            var result = await custom.PairAsync(kinds, DevicePairingProtectionLevel.Default);

            return result.Status switch
            {
                DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired => PairOutcome.Paired,
                _ when cancelled => PairOutcome.Cancelled,
                DevicePairingResultStatus.PairingCanceled or DevicePairingResultStatus.RejectedByHandler => PairOutcome.Cancelled,
                DevicePairingResultStatus.AuthenticationFailure or DevicePairingResultStatus.AuthenticationNotAllowed
                    or DevicePairingResultStatus.ProtectionLevelCouldNotBeMet
                    or DevicePairingResultStatus.RequiredHandlerNotRegistered => PairOutcome.NeedsSettings,
                _ => PairOutcome.Failed,
            };
        }
        catch
        {
            return PairOutcome.Failed;
        }
    }

    public async Task<bool> UnpairAsync(string id)
    {
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(id);
            var result = await info.Pairing.UnpairAsync();
            return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SetAudioConnectedAsync(BluetoothDeviceItem device, bool connect)
    {
        if (device.Container is not { } container) return false;
        bool sent = await Task.Run(() => connect ? BluetoothAudio.Connect(container) : BluetoothAudio.Disconnect(container));

        // The driver answers at once but the connection follows a moment later.
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(700);
            await RefreshAudioAsync();
            if (_audio.GetValueOrDefault(container) == connect) break;
        }
        return sent;
    }

    async Task RefreshAudioAsync()
    {
        var audio = await Task.Run(BluetoothAudio.List);
        lock (_gate)
        {
            _audio = audio;
            Publish();
        }
        Changed?.Invoke();
    }

    static string Selector(string protocol, bool paired)
        => $"System.Devices.Aep.ProtocolId:=\"{protocol}\" AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#{(paired ? "True" : "False")}";

    DeviceWatcher? Watch(string selector, Dictionary<string, DeviceInformation> into)
    {
        try
        {
            var watcher = DeviceInformation.CreateWatcher(selector, Properties, DeviceInformationKind.AssociationEndpoint);
            watcher.Added += (_, info) => Update(() =>
            {
                if (into != _nearby || IsScanning) into[info.Id] = info;
            }, audioMayChange: into == _paired);
            watcher.Updated += (_, update) => Update(() =>
            {
                if (into.TryGetValue(update.Id, out var info)) info.Update(update);
            }, audioMayChange: into == _paired);
            watcher.Removed += (_, update) => Update(() => into.Remove(update.Id), audioMayChange: false);
            watcher.Start();
            return watcher;
        }
        catch
        {
            return null;
        }
    }

    void Update(Action change, bool audioMayChange)
    {
        lock (_gate)
        {
            change();
            Publish();
        }
        Changed?.Invoke();
        if (audioMayChange) ScheduleAudioRefresh();
    }

    void ScheduleAudioRefresh()
    {
        lock (_gate)
        {
            _audioRefresh ??= new Timer(_ => _ = RefreshAudioAsync());
            _audioRefresh.Change(400, Timeout.Infinite);
        }
    }

    /// <summary>Call with the lock held.</summary>
    void Publish()
    {
        Paired = Items(_paired.Values);
        Nearby = Items(_nearby.Values.Where(info => !_paired.ContainsKey(info.Id)));
    }

    List<BluetoothDeviceItem> Items(IEnumerable<DeviceInformation> infos)
        => infos
            .Where(info => !string.IsNullOrWhiteSpace(info.Name))
            .Select(ToItem)
            // Headphones often appear twice, as classic and as low-energy Bluetooth.
            .GroupBy(item => item.Name)
            .Select(group => group.OrderByDescending(item => item.HasAudio).ThenByDescending(item => item.IsConnected).First())
            .OrderByDescending(item => item.IsConnected)
            .ThenBy(item => item.Name)
            .ToList();

    BluetoothDeviceItem ToItem(DeviceInformation info)
    {
        var properties = info.Properties;
        Guid? container = properties.TryGetValue(ContainerKey, out var c) && c is Guid g ? g : null;
        bool hasAudio = container is { } id && _audio.ContainsKey(id);
        bool connected = properties.TryGetValue(IsConnectedKey, out var on) && on is true
                      || container is { } cid && _audio.GetValueOrDefault(cid);

        var kind = hasAudio ? BluetoothKind.Audio : KindOf(properties);
        return new BluetoothDeviceItem(info.Id, info.Name, kind, connected, hasAudio, container);
    }

    static BluetoothKind KindOf(IReadOnlyDictionary<string, object> properties)
    {
        if (properties.TryGetValue(MajorClassKey, out var major) && major != null)
        {
            return Convert.ToUInt32(major) switch
            {
                1 => BluetoothKind.Computer,
                2 => BluetoothKind.Phone,
                4 => BluetoothKind.Audio,
                5 => BluetoothKind.Input,
                _ => BluetoothKind.Other,
            };
        }

        if (properties.TryGetValue(AppearanceKey, out var category) && category != null)
        {
            return Convert.ToUInt32(category) switch
            {
                1 => BluetoothKind.Phone,
                2 => BluetoothKind.Computer,
                15 => BluetoothKind.Input,
                _ => BluetoothKind.Other,
            };
        }

        return BluetoothKind.Other;
    }
}
