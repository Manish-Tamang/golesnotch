using System.Runtime.InteropServices;

namespace Notch.Interop;

/// <summary>
/// Connects and disconnects Bluetooth headphones and speakers, as the Connect button in
/// Windows' Bluetooth settings does. Windows has no public API for this; the audio driver
/// takes a one-shot "reconnect" or "disconnect" request on each of the device's audio
/// endpoints (the approach used by ToothTray, github.com/m2jean/ToothTray).
/// </summary>
/// <remarks>Blocking; call from a background thread.</remarks>
static class BluetoothAudio
{
    const int AllFlows = 2;
    const int Active = 1, Unplugged = 8;
    const int ClassContext = 23;
    const ushort VtClsid = 72;
    const uint KsPropertyTypeGet = 1;
    const uint OneShotReconnect = 0, OneShotDisconnect = 1;

    static readonly Guid BtAudioPropertySet = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    static readonly PropertyKey ContainerIdKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

    /// <summary>The audio endpoints of Bluetooth devices, by container id, with whether any is connected.</summary>
    public static Dictionary<Guid, bool> List()
    {
        var result = new Dictionary<Guid, bool>();
        foreach (var (container, connected, _) in Endpoints())
            result[container] = result.GetValueOrDefault(container) || connected;
        return result;
    }

    public static bool Connect(Guid container) => Send(container, OneShotReconnect);

    public static bool Disconnect(Guid container) => Send(container, OneShotDisconnect);

    static bool Send(Guid container, uint request)
    {
        bool sent = false;
        foreach (var (id, _, control) in Endpoints())
        {
            if (id != container) continue;
            var property = new KsPropertyHeader { Set = BtAudioPropertySet, Id = request, Flags = KsPropertyTypeGet };
            if (control.KsProperty(ref property, (uint)Marshal.SizeOf<KsPropertyHeader>(), IntPtr.Zero, 0, out _) >= 0)
                sent = true;
        }
        return sent;
    }

    static IEnumerable<(Guid Container, bool Connected, IKsControl Control)> Endpoints()
    {
        var found = new List<(Guid, bool, IKsControl)>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.EnumAudioEndpoints(AllFlows, Active | Unplugged, out var endpoints) != 0 || endpoints == null)
                return found;

            endpoints.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    if (endpoints.Item(i, out var endpoint) != 0 || endpoint == null) continue;
                    if (Inspect(enumerator, endpoint) is { } item) found.Add(item);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return found;
    }

    static (Guid, bool, IKsControl)? Inspect(IMMDeviceEnumerator enumerator, IMMDevice endpoint)
    {
        var topologyId = typeof(IDeviceTopology).GUID;
        if (endpoint.Activate(ref topologyId, ClassContext, IntPtr.Zero, out var instance) != 0
            || instance is not IDeviceTopology topology)
            return null;
        if (topology.GetConnector(0, out var connector) != 0 || connector == null) return null;
        if (connector.GetDeviceIdConnectedTo(out var filterId) != 0 || filterId == null) return null;
        if (!filterId.Contains("BTH", StringComparison.OrdinalIgnoreCase)) return null;

        if (enumerator.GetDevice(filterId, out var filter) != 0 || filter == null) return null;
        var controlId = typeof(IKsControl).GUID;
        if (filter.Activate(ref controlId, ClassContext, IntPtr.Zero, out var controlInstance) != 0
            || controlInstance is not IKsControl control)
            return null;

        if (ContainerOf(endpoint) is not { } container) return null;
        endpoint.GetState(out int state);
        return (container, state == Active, control);
    }

    static Guid? ContainerOf(IMMDevice endpoint)
    {
        if (endpoint.OpenPropertyStore(0, out var store) != 0 || store == null) return null;

        var key = ContainerIdKey;
        if (store.GetValue(ref key, out var value) != 0) return null;
        try
        {
            return value.Type == VtClsid && value.Pointer != IntPtr.Zero
                ? Marshal.PtrToStructure<Guid>(value.Pointer)
                : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }
}
