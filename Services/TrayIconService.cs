using System.Collections.Specialized;
using System.ComponentModel;
using ManagedShell.WindowsTray;
using MsTrayService = ManagedShell.WindowsTray.TrayService;

namespace Notch.Services;

/// <summary>
/// The icons apps put in the Windows system tray, for shells such as Seelen UI that hide
/// the taskbar and its tray. ManagedShell registers a hidden Shell_TrayWnd above Explorer's,
/// so apps send their icons here, and forwards every message on to Explorer, which keeps
/// working as before. UI thread only.
/// </summary>
public sealed class TrayIconService
{
    static readonly HashSet<Guid> SystemIcons = new[]
    {
        NotificationArea.VOLUME_GUID,
        NotificationArea.NETWORK_GUID,
        NotificationArea.POWER_GUID,
        NotificationArea.HEALTH_GUID,
        NotificationArea.MEETNOW_GUID,
    }.Select(Guid.Parse).ToHashSet();

    NotificationArea? _area;

    public bool IsRunning => _area != null;

    /// <summary>The visible app icons, in the order they were added.</summary>
    public IReadOnlyList<NotifyIcon> Icons => _area == null
        ? Array.Empty<NotifyIcon>()
        : _area.TrayIcons.Where(icon => !icon.IsHidden && !SystemIcons.Contains(icon.GUID)).ToList();

    /// <summary>An icon was added, removed, hidden, or changed its picture or name.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (_area != null) return;

        try
        {
            var area = new NotificationArea(Array.Empty<string>(), new MsTrayService(), new ExplorerTrayService());
            // ManagedShell's icon list is shared between instances; drop any left from a previous run.
            area.TrayIcons.Clear();
            area.TrayIcons.CollectionChanged += OnIconsChanged;
            area.Initialize();
            if (area.IsFailed)
            {
                area.TrayIcons.CollectionChanged -= OnIconsChanged;
                area.Dispose();
                return;
            }
            _area = area;
        }
        catch
        {
            _area = null;
        }

        Changed?.Invoke();
    }

    /// <summary>Hands the tray back to Explorer; apps re-add their icons there.</summary>
    public void Stop()
    {
        if (_area == null) return;

        var area = _area;
        _area = null;
        area.TrayIcons.CollectionChanged -= OnIconsChanged;
        foreach (var icon in area.TrayIcons) icon.PropertyChanged -= OnIconChanged;

        try
        {
            area.Dispose();
        }
        catch
        {
            // Already gone; Explorer gets the icons back either way once our window closes.
        }

        area.TrayIcons.Clear();
        Changed?.Invoke();
    }

    void OnIconsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (NotifyIcon icon in e.OldItems) icon.PropertyChanged -= OnIconChanged;
        if (e.NewItems != null)
            foreach (NotifyIcon icon in e.NewItems) icon.PropertyChanged += OnIconChanged;
        Changed?.Invoke();
    }

    void OnIconChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotifyIcon.Icon) or nameof(NotifyIcon.Title) or nameof(NotifyIcon.IsHidden))
            Changed?.Invoke();
    }
}
