using System.Diagnostics;
using System.Windows.Media.Imaging;
using Notch.Interop;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Notch.Services;

/// <summary>One Windows toast notification, reduced to what the notch shows.</summary>
public sealed record NotificationItem(
    uint Id,
    string AppName,
    string AppId,
    string Title,
    string Body,
    DateTimeOffset Time,
    BitmapImage? Icon);

public enum NotificationAccess
{
    /// <summary>Not asked yet.</summary>
    Unknown,
    Allowed,
    /// <summary>The user (or a policy) said no. Can be changed in Windows Settings.</summary>
    Denied,
    /// <summary>This Windows build doesn't let the app listen at all.</summary>
    Unavailable,
}

/// <summary>
/// Mirrors the notifications in Windows' notification centre, using the
/// UserNotificationListener API. Nothing is stored or sent anywhere.
/// </summary>
/// <remarks>
/// The API's change event only works for packaged (Store-style) apps, so this polls
/// instead. Start it from the UI thread; all events are then raised on the UI thread.
/// </remarks>
public sealed class NotificationService
{
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);

    readonly Dictionary<uint, NotificationItem> _known = new();
    readonly HashSet<uint> _unreadable = new();
    // Notifications the user dismissed here. Windows sometimes keeps them in its own
    // list anyway, so they are also remembered and skipped for as long as they exist.
    readonly HashSet<uint> _dismissed = new();
    readonly Dictionary<string, BitmapImage?> _icons = new();
    UserNotificationListener? _listener;
    bool _polling;
    bool _firstPoll = true;

    public NotificationAccess Access { get; private set; } = NotificationAccess.Unknown;

    /// <summary>Current notifications, newest first.</summary>
    public IReadOnlyList<NotificationItem> Items { get; private set; } = Array.Empty<NotificationItem>();

    /// <summary>The list or the access state changed.</summary>
    public event Action? Changed;

    /// <summary>A notification that wasn't there before has appeared.</summary>
    public event Action<NotificationItem>? Arrived;

    /// <summary>Asks Windows for access and starts watching. Safe to call again after a refusal.</summary>
    public async Task StartAsync()
    {
        if (_polling) return;

        try
        {
            _listener = UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync();
            Access = status == UserNotificationListenerAccessStatus.Allowed
                ? NotificationAccess.Allowed
                : NotificationAccess.Denied;
        }
        catch
        {
            Access = NotificationAccess.Unavailable;
        }

        Changed?.Invoke();

        if (Access == NotificationAccess.Allowed && !_polling)
        {
            _polling = true;
            _ = PollLoopAsync();
        }
    }

    /// <summary>Removes a notification here and, where Windows allows, in its notification centre.</summary>
    public void Dismiss(uint id)
    {
        Remove(id);
        if (_known.Remove(id))
            Publish();
    }

    /// <summary>Clears every notification here and, where Windows allows, in its notification centre.</summary>
    public void ClearAll()
    {
        foreach (uint id in _known.Keys)
            Remove(id);

        try
        {
            _listener?.ClearNotifications();
        }
        catch
        {
            // Each one was already removed individually above.
        }

        if (_known.Count > 0)
        {
            _known.Clear();
            Publish();
        }
    }

    void Remove(uint id)
    {
        _dismissed.Add(id);
        try
        {
            _listener?.RemoveNotification(id);
        }
        catch
        {
            // Windows refused or it is already gone; it stays hidden here either way.
        }
    }

    /// <summary>
    /// Brings up the app that sent a notification. Windows has no way for another app
    /// to trigger the notification's own click action, so this opens the app itself.
    /// </summary>
    public static void OpenApp(NotificationItem item)
    {
        if (string.IsNullOrEmpty(item.AppId)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"shell:AppsFolder\\{item.AppId}\"")
            {
                UseShellExecute = false,
            });
        }
        catch
        {
            // The app can't be launched this way.
        }
    }

    async Task PollLoopAsync()
    {
        while (true)
        {
            try
            {
                await PollAsync();
            }
            catch
            {
                // A failed read is retried on the next round.
            }

            await Task.Delay(PollInterval);
        }
    }

    async Task PollAsync()
    {
        if (_listener == null) return;

        var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
        var seen = new HashSet<uint>();
        var arrived = new List<NotificationItem>();
        bool changed = false;

        foreach (var notification in current)
        {
            uint id = notification.Id;
            seen.Add(id);
            if (_known.ContainsKey(id) || _unreadable.Contains(id) || _dismissed.Contains(id)) continue;

            var item = await ReadAsync(notification);
            if (item == null)
            {
                _unreadable.Add(id);
                continue;
            }

            _known[id] = item;
            changed = true;

            // Whatever was already waiting when the app started isn't news.
            if (!_firstPoll) arrived.Add(item);
        }

        foreach (uint id in _known.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _known.Remove(id);
            changed = true;
        }
        _unreadable.IntersectWith(seen);
        _dismissed.IntersectWith(seen);
        _firstPoll = false;

        if (changed) Publish();
        foreach (var item in arrived) Arrived?.Invoke(item);
    }

    void Publish()
    {
        Items = _known.Values.OrderByDescending(item => item.Time).ToList();
        Changed?.Invoke();
    }

    async Task<NotificationItem?> ReadAsync(UserNotification notification)
    {
        var binding = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
        if (binding == null) return null;

        // By convention the first text is the title and the rest is the message.
        var texts = binding.GetTextElements()
            .Select(element => element.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        if (texts.Count == 0) return null;

        string appName = "", appId = "";
        BitmapImage? icon = null;
        try
        {
            var app = notification.AppInfo;
            appId = app.AppUserModelId ?? "";
            appName = app.DisplayInfo.DisplayName ?? "";

            if (!_icons.TryGetValue(appId, out icon))
            {
                var logo = app.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
                icon = await WinRtImage.LoadAsync(logo, decodeWidth: 72);
                _icons[appId] = icon;
            }
        }
        catch
        {
            // Some desktop apps expose no name or logo; the card falls back to a letter.
        }

        return new NotificationItem(
            notification.Id,
            appName,
            appId,
            texts[0],
            string.Join("\n", texts.Skip(1)),
            notification.CreationTime,
            icon);
    }
}
