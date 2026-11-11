using System.Diagnostics;
using System.Windows;
using Notch.Services;

namespace Notch.Views;

/// <summary>The list of current Windows notifications. Click one to enlarge it.</summary>
public partial class NotificationsPage : NotchPage
{
    const double HeaderHeight = 42;
    const double MaxListHeight = 232; // matches the ScrollViewer's MaxHeight
    const double BottomPadding = 4;
    const int MaxItems = 20;

    readonly NotificationService _service;
    readonly HashSet<uint> _enlarged = new();

    public NotificationsPage(AppServices app)
    {
        InitializeComponent();
        IconGlyph = "\uEA8F";
        _service = app.Notifications;
        _service.Changed += () => Dispatcher.InvokeAsync(Rebuild);
        Rebuild();
    }

    public override void OnShown()
    {
        // The user may have granted access in Windows Settings since we last asked.
        if (_service.Access is NotificationAccess.Denied or NotificationAccess.Unavailable)
            _ = _service.StartAsync();
    }

    void Rebuild()
    {
        CardList.Children.Clear();

        switch (_service.Access)
        {
            case NotificationAccess.Unknown:
                ShowMessage("Checking notification access…", offerSettings: false);
                return;
            case NotificationAccess.Denied:
                ShowMessage("Notch isn't allowed to read notifications yet. Turn on notification "
                          + "access for it in Windows Settings, then reopen this page.", offerSettings: true);
                return;
            case NotificationAccess.Unavailable:
                ShowMessage("Windows didn't let Notch read notifications on this PC.", offerSettings: true);
                return;
        }

        var items = _service.Items.Take(MaxItems).ToList();
        _enlarged.IntersectWith(items.Select(item => item.Id));

        if (items.Count == 0)
        {
            ShowMessage("No notifications", offerSettings: false);
            return;
        }

        foreach (var item in items)
        {
            var card = new NotificationCard();
            card.Show(item, _enlarged.Contains(item.Id));
            card.EnlargedChanged += OnCardEnlargedChanged;
            card.OpenRequested += NotificationService.OpenApp;
            card.DismissRequested += dismissed => _service.Dismiss(dismissed.Id);
            CardList.Children.Add(card);
        }

        HeaderText.Text = $"Notifications  ·  {_service.Items.Count}";
        ClearButton.Visibility = Visibility.Visible;
        Scroller.Visibility = Visibility.Visible;
        MessagePanel.Visibility = Visibility.Collapsed;
        UpdateHeight();
    }

    void ShowMessage(string text, bool offerSettings)
    {
        HeaderText.Text = "Notifications";
        MessageText.Text = text;
        AccessButton.Visibility = offerSettings ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = Visibility.Collapsed;
        Scroller.Visibility = Visibility.Collapsed;
        MessagePanel.Visibility = Visibility.Visible;
        UpdateHeight();
    }

    void OnCardEnlargedChanged(NotificationCard card)
    {
        if (card.Item is { } item)
        {
            if (card.IsEnlarged) _enlarged.Add(item.Id);
            else _enlarged.Remove(item.Id);
        }
        UpdateHeight();
    }

    /// <summary>Measures the visible content so the notch can grow to fit it.</summary>
    void UpdateHeight()
    {
        FrameworkElement content = MessagePanel.Visibility == Visibility.Visible ? MessagePanel : CardList;
        content.Measure(new Size(PageWidth, double.PositiveInfinity));

        double height = Math.Min(MaxListHeight, Math.Ceiling(content.DesiredSize.Height));
        PageHeight = HeaderHeight + height + BottomPadding;
    }

    void Clear_Click(object sender, RoutedEventArgs e) => _service.ClearAll();

    void Access_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true });
        }
        catch
        {
            // The Settings app is missing or blocked.
        }
    }
}
