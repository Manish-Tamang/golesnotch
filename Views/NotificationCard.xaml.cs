using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Notch.Services;

namespace Notch.Views;

/// <summary>
/// A notification, used both as the popup and as a row on the notifications page.
/// Compact by default; click to enlarge.
/// </summary>
public partial class NotificationCard : UserControl
{
    const double MaxBodyHeight = 150;

    public NotificationCard()
    {
        InitializeComponent();
    }

    public NotificationItem? Item { get; private set; }
    public bool IsEnlarged { get; private set; }

    /// <summary>The card was clicked and changed size.</summary>
    public event Action<NotificationCard>? EnlargedChanged;
    public event Action<NotificationItem>? OpenRequested;
    public event Action<NotificationItem>? DismissRequested;

    public void Show(NotificationItem item, bool enlarged)
    {
        Item = item;

        AppText.Text = item.AppName;
        TimeText.Text = RelativeTime(item.Time);
        TitleText.Text = item.Title;

        IconImage.Source = item.Icon;
        IconFallback.Visibility = item.Icon == null ? Visibility.Visible : Visibility.Collapsed;
        string name = item.AppName.Length > 0 ? item.AppName : item.Title;
        IconLetter.Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "";

        SetEnlarged(enlarged);
    }

    public void SetEnlarged(bool enlarged)
    {
        IsEnlarged = enlarged;
        string body = Item?.Body ?? "";

        // Compact: one line each. Enlarged: the whole message, wrapped.
        TitleText.TextWrapping = enlarged ? TextWrapping.Wrap : TextWrapping.NoWrap;
        BodyText.TextWrapping = enlarged ? TextWrapping.Wrap : TextWrapping.NoWrap;
        BodyText.MaxHeight = enlarged ? MaxBodyHeight : double.PositiveInfinity;
        BodyText.Text = enlarged ? body : body.ReplaceLineEndings(" ");
        BodyText.Visibility = body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Actions.Visibility = enlarged ? Visibility.Visible : Visibility.Collapsed;
    }

    void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetEnlarged(!IsEnlarged);
        EnlargedChanged?.Invoke(this);
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Item != null) OpenRequested?.Invoke(Item);
    }

    void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (Item != null) DismissRequested?.Invoke(Item);
    }

    static string RelativeTime(DateTimeOffset time)
    {
        var age = DateTimeOffset.Now - time;
        if (age < TimeSpan.FromMinutes(1)) return "now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes}m";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours}h";
        return time.LocalDateTime.ToString("d MMM");
    }
}
