using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace Notch.Services;

/// <summary>
/// Turns Wi-Fi and Bluetooth on and off, the same switches as in Windows' quick settings.
/// </summary>
/// <remarks>Start it from the UI thread. <see cref="Changed"/> can be raised on any thread.</remarks>
public sealed class RadioService
{
    Radio? _wifi;
    Radio? _bluetooth;

    /// <summary>A radio was switched, here or anywhere else in Windows.</summary>
    public event Action? Changed;

    /// <summary>Null when this PC has no such radio, or Windows won't let the app control it.</summary>
    public bool? IsOn(RadioKind kind) => Find(kind) is { } radio ? radio.State == RadioState.On : null;

    /// <summary>The name of the Wi-Fi network the PC is connected to, or null.</summary>
    public string? WifiNetwork
    {
        get
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                return profile is { IsWlanConnectionProfile: true }
                    ? profile.WlanConnectionProfileDetails.GetConnectedSsid()
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public async Task StartAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed)
            {
                foreach (var radio in await Radio.GetRadiosAsync())
                {
                    if (radio.Kind == RadioKind.WiFi) _wifi ??= radio;
                    else if (radio.Kind == RadioKind.Bluetooth) _bluetooth ??= radio;
                    else continue;

                    radio.StateChanged += (_, _) => Changed?.Invoke();
                }
            }
        }
        catch
        {
            // Radios can't be controlled on this PC; the switches show as unavailable.
        }

        Changed?.Invoke();
    }

    /// <returns>False when Windows refused.</returns>
    public async Task<bool> SetAsync(RadioKind kind, bool on)
    {
        if (Find(kind) is not { } radio) return false;

        try
        {
            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            return result == RadioAccessStatus.Allowed;
        }
        catch
        {
            return false;
        }
    }

    Radio? Find(RadioKind kind) => kind switch
    {
        RadioKind.WiFi => _wifi,
        RadioKind.Bluetooth => _bluetooth,
        _ => null,
    };
}
