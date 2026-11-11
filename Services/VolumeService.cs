using System.Runtime.InteropServices;
using Notch.Interop;

namespace Notch.Services;

/// <summary>
/// The system volume of the default speakers or headphones: its level, whether it is
/// muted, and a notification whenever either changes.
/// </summary>
/// <remarks>
/// Events are raised on background threads. All audio calls are made from background
/// threads too, which is what the audio API expects.
/// </remarks>
public sealed class VolumeService
{
    const int RenderDevices = 0;   // playback, as opposed to recording
    const int ConsoleRole = 0;     // the device system sounds and the volume keys use
    const int AllContexts = 23;

    readonly object _gate = new();
    readonly Listener _listener;
    IAudioEndpointVolume? _endpoint;
    string? _deviceId;
    Timer? _rebind;

    public VolumeService()
    {
        _listener = new Listener(OnNotified);
    }

    /// <summary>0 to 1.</summary>
    public double Level { get; private set; }
    public bool IsMuted { get; private set; }

    /// <summary>The level or mute state is now known or has changed.</summary>
    public event Action? Changed;

    /// <summary>Somebody changed the volume: the volume keys, an app, the notch itself.</summary>
    public event Action? Adjusted;

    public void Start()
    {
        // The default device changes when headphones are plugged in or a Bluetooth
        // speaker connects, so look again every few seconds and follow it.
        _rebind = new Timer(_ => Bind(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public void ToggleMute()
    {
        Task.Run(() =>
        {
            IAudioEndpointVolume? endpoint;
            bool mute;
            lock (_gate)
            {
                endpoint = _endpoint;
                mute = !IsMuted;
            }

            try
            {
                var context = Guid.Empty;
                endpoint?.SetMute(mute, ref context);
            }
            catch
            {
                // The device went away.
            }
        });
    }

    /// <summary>Attaches to the current default playback device, if it isn't the one already followed.</summary>
    void Bind()
    {
        bool bound = false;

        lock (_gate)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (enumerator.GetDefaultAudioEndpoint(RenderDevices, ConsoleRole, out var device) != 0 || device == null)
                    return;

                device.GetId(out string? id);
                if (id == _deviceId && _endpoint != null) return;

                var interfaceId = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref interfaceId, AllContexts, IntPtr.Zero, out object? instance) != 0
                    || instance is not IAudioEndpointVolume endpoint)
                    return;

                try
                {
                    _endpoint?.UnregisterControlChangeNotify(_listener);
                }
                catch
                {
                    // The old device is already gone.
                }

                endpoint.RegisterControlChangeNotify(_listener);
                endpoint.GetMasterVolumeLevelScalar(out float level);
                endpoint.GetMute(out bool muted);

                _endpoint = endpoint;
                _deviceId = id;
                Level = level;
                IsMuted = muted;
                bound = true;
            }
            catch
            {
                // No audio device, or the audio service is restarting. Tried again shortly.
            }
        }

        if (bound) Changed?.Invoke();
    }

    void OnNotified(float level, bool muted)
    {
        lock (_gate)
        {
            // Windows also reports changes that leave the level where it was.
            if (Math.Abs(level - Level) < 0.004 && muted == IsMuted) return;
            Level = level;
            IsMuted = muted;
        }

        Changed?.Invoke();
        Adjusted?.Invoke();
    }

    /// <summary>The object Windows calls back into.</summary>
    sealed class Listener : IAudioEndpointVolumeCallback
    {
        readonly Action<float, bool> _notified;

        public Listener(Action<float, bool> notified) => _notified = notified;

        public int OnNotify(IntPtr data)
        {
            if (data != IntPtr.Zero)
            {
                // The data starts with a 16-byte id, then the mute flag, then the level.
                bool muted = Marshal.ReadInt32(data, 16) != 0;
                float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
                _notified(level, muted);
            }
            return 0;
        }
    }
}
