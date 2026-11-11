using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Notch.Core;
using Notch.Interop;
using Notch.Services;

namespace Notch.Views;

/// <summary>
/// The notch itself. It is always in one of three modes:
/// <list type="bullet">
/// <item><b>Collapsed</b>: the small pill.</item>
/// <item><b>Hud</b>: a short popup (charger, timer, notification) that closes by itself.</item>
/// <item><b>Expanded</b>: open, showing one of the pages.</item>
/// </list>
/// Each mode has a target size for the black island. Changing mode sends the island
/// towards that size and cross-fades the matching view in.
/// </summary>
public partial class NotchWindow : Window
{
    // Sizes are in device-independent pixels.
    const double CollapsedHeight = 32;
    const double LyricPillWidth = 340;   // the closed notch while it shows a lyric line
    const double HudWidth = 380;         // matches HudView in the XAML
    const double BannerHeight = 60;
    const double VolumeHeight = 40;
    const double DotsHeight = 16;        // height of the page-dots row
    const double DotsRoom = 14;          // extra island height when the dots are showing
    const double TabsHeight = 30;        // height of the page-icon capsule
    const double TabsBottom = 8;         // its distance from the island's bottom edge
    const double TabsRoom = 26;          // extra island height while the icons are showing
    const double HiddenScale = 0.94;     // views shrink slightly while faded out

    // A fullscreen window has to stay in front this long before the notch hides for it.
    // Switchers, launchers and screenshot overlays cover the screen only briefly.
    static readonly TimeSpan FullscreenGrace = TimeSpan.FromSeconds(1.2);

    enum Mode { Collapsed, Hud, Expanded }

    readonly AppServices _app;
    readonly MusicPage _music;
    readonly NotificationsPage _notifications;
    readonly TimerPage _timer;
    readonly DevicesPage _devices;
    readonly NotchPage[] _allPages;
    readonly HudBanner _banner = new();
    readonly NotificationCard _hudCard = new();
    readonly VolumeBanner _volumeBanner = new();
    readonly List<NotchPage> _pages = new();

    readonly ScaleTransform _hudScale = new(HiddenScale, HiddenScale);
    readonly ScaleTransform _expandedScale = new(HiddenScale, HiddenScale);
    readonly TranslateTransform _dotsShift = new();
    readonly TranslateTransform _tabsShift = new();

    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _openDelay = new() { Interval = TimeSpan.FromMilliseconds(90) };
    readonly DispatcherTimer _closeDelay = new() { Interval = TimeSpan.FromMilliseconds(260) };
    readonly DispatcherTimer _tabsCloseDelay = new() { Interval = TimeSpan.FromMilliseconds(320) };
    readonly DispatcherTimer _hudTimer = new();
    readonly DispatcherTimer _fullscreenCheck = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly DispatcherTimer _fullscreenConfirm = new() { Interval = FullscreenGrace + TimeSpan.FromMilliseconds(50) };

    Mode _mode = Mode.Collapsed;
    int _pageIndex;
    double _hudHeight = BannerHeight;
    bool _ready;
    bool _hiddenForFullscreen;
    DateTime? _fullscreenSince;

    bool _tabsOpen;
    bool _tabsPlaced;   // the icon row is already on screen, so move it rather than place it

    NowPlaying? _np;
    BitmapImage? _pillArt;
    bool _pillArtShown;
    bool _lyricPill;    // the closed notch is showing a lyric line

    IntPtr _handle;
    Action? _bannerAction;   // what clicking the current status popup does; null opens the notch

    // Scrolling between pages
    DateTime _lastPageSwitch;
    DateTime _lastSwipeStep;
    double _swipeDistance;
    bool _swipeUsed;

    SettingsWindow? _settingsWindow;

    public NotchWindow(AppServices app)
    {
        InitializeComponent();
        _app = app;

        // The island's outline also clips its content, so nothing spills outside the black.
        ContentHost.Clip = IslandBody.Outline;
        IslandBody.Moved += OnIslandMoved;
        HudView.RenderTransform = _hudScale;
        ExpandedView.RenderTransform = _expandedScale;
        Dots.RenderTransform = _dotsShift;
        Tabs.RenderTransform = _tabsShift;

        _music = new MusicPage(app);
        _notifications = new NotificationsPage(app);
        _timer = new TimerPage(app);
        _devices = new DevicesPage(app);
        _allPages = new NotchPage[] { _music, _notifications, _timer, _devices };
        foreach (var page in _allPages)
        {
            page.RenderTransform = new TranslateTransform();
            page.Visibility = Visibility.Collapsed;
            page.PageSizeChanged += OnPageSizeChanged;
            PageHost.Children.Add(page);
        }

        // The three things a popup can hold; one is visible at a time.
        foreach (UIElement content in new UIElement[] { _banner, _hudCard, _volumeBanner })
        {
            content.Visibility = Visibility.Collapsed;
            HudView.Children.Add(content);
        }
        _hudCard.EnlargedChanged += OnHudCardEnlargedChanged;
        _hudCard.OpenRequested += item =>
        {
            NotificationService.OpenApp(item);
            SetMode(Mode.Collapsed);
        };
        _hudCard.DismissRequested += item =>
        {
            _app.Notifications.Dismiss(item.Id);
            SetMode(Mode.Collapsed);
        };

        // Page dots, and the icon row that replaces them on hover.
        Dots.Selected += GoToPage;
        Dots.MouseEnter += (_, _) => OnDotsHovered();
        Dots.MouseLeave += (_, _) => Restart(_tabsCloseDelay);
        Tabs.Selected += GoToPage;
        Tabs.SettingsRequested += OpenSettings;
        Tabs.MouseEnter += (_, _) => _tabsCloseDelay.Stop();
        Tabs.MouseLeave += (_, _) => Restart(_tabsCloseDelay);

        _clock.Tick += (_, _) => UpdatePill();
        _openDelay.Tick += (_, _) => OnOpenDelayElapsed();
        _closeDelay.Tick += (_, _) => OnCloseDelayElapsed();
        _tabsCloseDelay.Tick += (_, _) => OnTabsCloseDelayElapsed();
        _hudTimer.Tick += (_, _) => OnHudTimeout();
        _fullscreenCheck.Tick += (_, _) => OnForegroundChanged(); // a fallback for the foreground hook
        _fullscreenConfirm.Tick += (_, _) => CheckFullscreen();

        // Services may raise events on background threads; hop to the UI thread first.
        app.Settings.Changed += ApplySettings;
        app.Media.Changed += np => Dispatcher.InvokeAsync(() =>
        {
            _np = np;
            UpdatePill();
        });
        app.Power.Changed += () => Dispatcher.InvokeAsync(UpdatePill);
        app.Power.PluggedChanged += plugged => Dispatcher.InvokeAsync(() => ShowChargerBanner(plugged));
        app.Power.LowBattery += () => Dispatcher.InvokeAsync(ShowLowBatteryBanner);
        app.Notifications.Arrived += item => Dispatcher.InvokeAsync(() => ShowNotification(item));
        app.Devices.Connected += device => Dispatcher.InvokeAsync(() => ShowDeviceBanner(device, connected: true));
        app.Devices.Disconnected += device => Dispatcher.InvokeAsync(() => ShowDeviceBanner(device, connected: false));
        app.Volume.Adjusted += () => Dispatcher.InvokeAsync(ShowVolume);
        app.Timer.Changed += UpdatePill;
        app.Timer.Finished += ShowTimerFinished;
        app.LyricsSession.Changed += UpdatePill;
        app.LyricsSession.LineChanged += UpdatePill;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;

        BuildContextMenu();
    }

    AppSettings Settings => _app.Settings.Current;
    NotchPage CurrentPage => _pages[_pageIndex];

    // ================= Start-up =================

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        Native.MakeOverlay(_handle);

        // WPF has no event for sideways scrolling, so listen for the raw window message.
        HwndSource.FromHwnd(_handle)?.AddHook(OnWindowMessage);

        // Stay in front when another window comes forward, and react to fullscreen at once.
        Native.WatchForeground(OnForegroundChanged);
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionOnScreen();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        ApplySettings();
        ApplyViews(animate: false);
        _ready = true;

        _clock.Start();
        _fullscreenCheck.Start();
    }

    void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _settingsWindow?.Close();
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.InvokeAsync(PositionOnScreen);

    void PositionOnScreen()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    /// <summary>Runs at start-up and again whenever a setting changes.</summary>
    void ApplySettings()
    {
        var settings = Settings;

        Motion.SpeedFactor = settings.Speed switch
        {
            AnimationSpeed.Relaxed => 1.35,
            AnimationSpeed.Fast => 0.7,
            _ => 1.0,
        };
        Theme.ApplyAccent(settings.Accent);
        if (!settings.PageIcons) _tabsOpen = false;

        UpdatePages();
        UpdatePill();
        ApplyIsland(animate: _ready);
        if (_ready) ApplyViews(animate: true);
        UpdateGlow();
    }

    // ================= Modes =================

    void SetMode(Mode mode)
    {
        if (mode == _mode) return;

        if (_mode == Mode.Expanded) CurrentPage.OnHidden();
        _mode = mode;
        if (mode == Mode.Expanded) CurrentPage.OnShown();

        if (mode != Mode.Hud) _hudTimer.Stop();
        if (mode != Mode.Expanded)
        {
            _tabsOpen = false;
            _tabsCloseDelay.Stop();
        }

        UpdatePill();
        ApplyIsland(animate: true);
        ApplyViews(animate: true);
        UpdateGlow();
    }

    /// <summary>Sends the island towards the size the current mode asks for.</summary>
    void ApplyIsland(bool animate)
    {
        bool floating = Settings.Style == NotchStyle.Floating;
        double width, height, radius;

        switch (_mode)
        {
            case Mode.Hud:
                width = HudWidth;
                height = _hudHeight;
                radius = floating ? Math.Min(height / 2, 30) : 22;
                break;
            case Mode.Expanded:
                width = CurrentPage.PageWidth;
                height = CurrentPage.PageHeight
                       + (_pages.Count > 1 ? DotsRoom : 0)
                       + (_tabsOpen ? TabsRoom : 0);
                radius = floating ? 30 : 24;
                break;
            default:
                width = _lyricPill ? LyricPillWidth : Settings.CollapsedWidth;
                height = CollapsedHeight;
                radius = floating ? CollapsedHeight / 2 : 10;
                break;
        }

        Island.Margin = new Thickness(0, floating ? Settings.TopGap : 0, 0, 0);

        // The icon row appears in place, over the dots, and the island grows down around
        // it. Once it is showing it glides to follow page-height changes.
        double tabsY = height - TabsHeight - TabsBottom;
        if (animate && _tabsOpen && _tabsPlaced)
            Motion.To(_tabsShift, TranslateTransform.YProperty, tabsY, 420, Motion.Settle());
        else
            Motion.Set(_tabsShift, TranslateTransform.YProperty, tabsY);
        _tabsPlaced = _tabsOpen;

        IslandBody.MoveTo(width, height, radius, classic: !floating, animate);
    }

    /// <summary>Keeps the page dots just above the island's bottom edge as it moves.</summary>
    void OnIslandMoved() => _dotsShift.Y = IslandBody.CurrentHeight - DotsHeight;

    /// <summary>Fades in the view for the current mode and fades the others out.</summary>
    void ApplyViews(bool animate)
    {
        bool expanded = _mode == Mode.Expanded;

        FadeView(CollapsedView, null, _mode == Mode.Collapsed, animate);
        FadeView(HudView, _hudScale, _mode == Mode.Hud, animate);
        FadeView(ExpandedView, _expandedScale, expanded, animate);
        FadeView(Dots, null, expanded && _pages.Count > 1 && !_tabsOpen, animate);
        FadeView(Tabs, null, expanded && _tabsOpen, animate);
    }

    static void FadeView(UIElement view, ScaleTransform? scale, bool show, bool animate)
    {
        view.IsHitTestVisible = show;

        if (!animate)
        {
            Motion.Set(view, OpacityProperty, show ? 1 : 0);
            if (scale != null)
            {
                Motion.Set(scale, ScaleTransform.ScaleXProperty, show ? 1 : HiddenScale);
                Motion.Set(scale, ScaleTransform.ScaleYProperty, show ? 1 : HiddenScale);
            }
            return;
        }

        if (show)
        {
            // Wait a beat so the old view is gone, then bloom in as the island grows.
            Motion.To(view, OpacityProperty, 1, 260, delayMs: 80);
            if (scale != null)
            {
                Motion.To(scale, ScaleTransform.ScaleXProperty, 1, 620, Motion.Spring(), delayMs: 30);
                Motion.To(scale, ScaleTransform.ScaleYProperty, 1, 620, Motion.Spring(), delayMs: 30);
            }
        }
        else
        {
            Motion.To(view, OpacityProperty, 0, 110);
            if (scale != null)
            {
                Motion.To(scale, ScaleTransform.ScaleXProperty, HiddenScale, 240);
                Motion.To(scale, ScaleTransform.ScaleYProperty, HiddenScale, 240);
            }
        }
    }

    /// <summary>
    /// Tints the island with the album cover's colour while music is what it's showing:
    /// the open music page, or the closed notch with a lyric line.
    /// </summary>
    void UpdateGlow()
    {
        bool onMusicPage = _mode == Mode.Expanded && _pages.Count > 0 && CurrentPage == _music;
        bool onLyricPill = _mode == Mode.Collapsed && _lyricPill;

        Color? color = Settings.AlbumGlow && (onMusicPage || onLyricPill) ? _np?.ArtColor : null;
        IslandBody.SetGlow(color);
    }

    // ================= Pages =================

    /// <summary>Rebuilds the page list from the settings, keeping the current page if it survives.</summary>
    void UpdatePages()
    {
        var wanted = new List<NotchPage> { _music };
        if (Settings.ShowNotificationsPage) wanted.Add(_notifications);
        if (Settings.ShowTimerPage) wanted.Add(_timer);
        if (Settings.ShowDevicesPage) wanted.Add(_devices);
        if (wanted.SequenceEqual(_pages)) return;

        var current = _pages.Count > 0 ? CurrentPage : _music;
        if (_mode == Mode.Expanded && !wanted.Contains(current)) current.OnHidden();

        _pages.Clear();
        _pages.AddRange(wanted);
        _pageIndex = Math.Max(0, _pages.IndexOf(current));
        if (_mode == Mode.Expanded && CurrentPage != current) CurrentPage.OnShown();

        foreach (var page in _allPages)
        {
            bool visible = page == CurrentPage;
            page.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            page.IsHitTestVisible = visible;
            Motion.Set(page, OpacityProperty, 1);
            Motion.Set((TranslateTransform)page.RenderTransform, TranslateTransform.XProperty, 0);
        }

        Dots.SetCount(_pages.Count);
        Dots.SetActive(_pageIndex, animate: false);
        Tabs.SetPages(_pages.Select(page => page.IconGlyph).ToList());
        Tabs.SetActive(_pageIndex);
    }

    void GoToPage(int index)
    {
        index = Math.Clamp(index, 0, _pages.Count - 1);
        if (index == _pageIndex) return;

        int direction = index > _pageIndex ? 1 : -1;
        var from = CurrentPage;
        var to = _pages[index];
        _pageIndex = index;

        if (_mode == Mode.Expanded)
        {
            from.OnHidden();
            to.OnShown();
        }

        const double Slide = 28;
        var fromShift = (TranslateTransform)from.RenderTransform;
        var toShift = (TranslateTransform)to.RenderTransform;

        // The old page slides out one way while the new one springs in from the other.
        from.IsHitTestVisible = false;
        Motion.To(fromShift, TranslateTransform.XProperty, -direction * Slide, 220);
        Motion.To(from, OpacityProperty, 0, 130, completed: () =>
        {
            if (CurrentPage != from) from.Visibility = Visibility.Collapsed;
        });

        to.Visibility = Visibility.Visible;
        to.IsHitTestVisible = true;
        Motion.Set(to, OpacityProperty, 0);
        Motion.Set(toShift, TranslateTransform.XProperty, direction * Slide);
        Motion.To(to, OpacityProperty, 1, 240, delayMs: 60);
        Motion.To(toShift, TranslateTransform.XProperty, 0, 560, Motion.Spring());

        Dots.SetActive(index, animate: true);
        Tabs.SetActive(index);
        ApplyIsland(animate: true);
        UpdateGlow();
    }

    void OnPageSizeChanged(NotchPage page)
    {
        if (_ready && _mode == Mode.Expanded && _pages.Count > 0 && page == CurrentPage)
            ApplyIsland(animate: true);
    }

    // ================= Page icons =================

    void OnDotsHovered()
    {
        if (!Settings.PageIcons || _mode != Mode.Expanded) return;
        _tabsCloseDelay.Stop();
        SetTabsOpen(true);
    }

    void SetTabsOpen(bool open)
    {
        if (open == _tabsOpen) return;
        _tabsOpen = open;
        ApplyIsland(animate: true);
        ApplyViews(animate: true);
    }

    void OnTabsCloseDelayElapsed()
    {
        _tabsCloseDelay.Stop();

        // WPF only notices what is under a still mouse when the mouse next moves, so
        // check where the cursor really is before closing under it.
        if (_tabsOpen && !IsCursorOver(Tabs))
            SetTabsOpen(false);
    }

    static bool IsCursorOver(FrameworkElement element)
    {
        var point = Mouse.GetPosition(element);
        return point.X >= 0 && point.Y >= 0
            && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;
    }

    // ================= Mouse =================

    void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _closeDelay.Stop();

        if (_mode == Mode.Hud)
            _hudTimer.Stop(); // keep the popup up while it's being looked at
        else if (_mode == Mode.Collapsed && Settings.Open == OpenMode.Hover)
            Restart(_openDelay); // a short wait, so brushing past the top edge doesn't open it
    }

    void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        _openDelay.Stop();

        if (_mode == Mode.Expanded)
            Restart(_closeDelay);
        else if (_mode == Mode.Hud)
            StartHudTimer(TimeSpan.FromSeconds(1.2));
    }

    void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // The notification card handles its own clicks.
        bool banner = _mode == Mode.Hud && _banner.Visibility == Visibility.Visible;

        if (banner && _bannerAction is { } action)
        {
            // A popup with its own action, such as opening the drive that was just plugged in.
            action();
            SetMode(Mode.Collapsed);
        }
        else if (_mode == Mode.Collapsed || banner)
        {
            _openDelay.Stop();
            SetMode(Mode.Expanded);
        }
    }

    // Scrolling down, or swiping two fingers up, goes to the next page.
    void Island_MouseWheel(object sender, MouseWheelEventArgs e) => OnScroll(-e.Delta);

    /// <summary>Raw window messages. Used for sideways scrolling, which WPF doesn't surface.</summary>
    IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Native.WM_MOUSEHWHEEL && _mode == Mode.Expanded)
        {
            // The scroll amount is the signed upper half of wParam. Swiping two fingers
            // to the left scrolls right (a positive amount), which is the next page.
            int amount = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            OnScroll(amount);
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Switches page in response to scrolling. Positive means towards the next page.</summary>
    void OnScroll(int amount)
    {
        if (_mode != Mode.Expanded || _pages.Count < 2 || amount == 0) return;
        var now = DateTime.UtcNow;

        // A mouse wheel moves in whole notches: one page per notch.
        if (amount % Mouse.MouseWheelDeltaForOneLine == 0)
        {
            if ((now - _lastPageSwitch).TotalMilliseconds < 160) return;
            _lastPageSwitch = now;
            GoToPage(_pageIndex + Math.Sign(amount));
            return;
        }

        // A touchpad swipe is a stream of small steps that carries on for a moment after
        // the fingers lift. One swipe should move one page, so once it has, the rest of
        // the stream is ignored until there's a pause.
        const double SwipeThreshold = 45;
        const double SwipePauseMs = 180;

        if ((now - _lastSwipeStep).TotalMilliseconds > SwipePauseMs)
        {
            _swipeDistance = 0;
            _swipeUsed = false;
        }
        _lastSwipeStep = now;
        if (_swipeUsed) return;

        _swipeDistance += amount;
        if (Math.Abs(_swipeDistance) < SwipeThreshold) return;

        _swipeUsed = true;
        _lastPageSwitch = now;
        GoToPage(_pageIndex + Math.Sign(_swipeDistance));
    }

    void OnOpenDelayElapsed()
    {
        _openDelay.Stop();
        if (_mode == Mode.Collapsed && Island.IsMouseOver)
            SetMode(Mode.Expanded);
    }

    void OnCloseDelayElapsed()
    {
        _closeDelay.Stop();
        bool menuOpen = Island.ContextMenu?.IsOpen == true;
        if (_mode == Mode.Expanded && !Island.IsMouseOver && !menuOpen)
            SetMode(Mode.Collapsed);
    }

    static void Restart(DispatcherTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    // ================= Popups =================

    /// <summary>Switches the island to a popup of the given height for a while.</summary>
    void ShowHud(double height, TimeSpan duration)
    {
        _hudHeight = height;
        _openDelay.Stop();

        if (_mode == Mode.Hud)
            ApplyIsland(animate: true); // already showing one: just resize
        else
            SetMode(Mode.Hud);

        if (Island.IsMouseOver)
            _hudTimer.Stop();
        else
            StartHudTimer(duration);
    }

    bool CanShowHud => _ready && _mode != Mode.Expanded && !_hiddenForFullscreen;

    void StartHudTimer(TimeSpan duration)
    {
        _hudTimer.Stop();
        _hudTimer.Interval = duration;
        _hudTimer.Start();
    }

    void OnHudTimeout()
    {
        _hudTimer.Stop();
        if (_mode == Mode.Hud) SetMode(Mode.Collapsed);
    }

    /// <param name="onClick">What clicking the popup does. Null opens the notch.</param>
    void ShowBanner(string icon, double ring, bool warning, string title, string subtitle, string value,
                    Action? onClick = null, double seconds = 3.2)
    {
        if (!CanShowHud) return;

        _banner.Show(icon, ring, warning, title, subtitle, value);
        ShowHudContent(_banner);
        _bannerAction = onClick;
        ShowHud(BannerHeight, TimeSpan.FromSeconds(seconds));
    }

    void ShowDeviceBanner(ConnectedDevice device, bool connected)
    {
        if (!Settings.DevicePopups) return;

        string icon = DevicesPage.GlyphFor(device.Kind);
        if (!connected)
        {
            ShowBanner(icon, 0, false, device.Name, "Disconnected", "");
            return;
        }

        string subtitle = device.Kind switch
        {
            DeviceKind.Drive => "USB drive connected · click to open",
            DeviceKind.Portable => "Device connected · click to open",
            _ => "Bluetooth connected",
        };
        Action? onClick = device.Kind == DeviceKind.Bluetooth ? null : () => DeviceService.Open(device);
        ShowBanner(icon, 1, false, device.Name, subtitle, "", onClick, seconds: 6);
    }

    /// <summary>Makes one of the popup's contents visible and hides the others.</summary>
    void ShowHudContent(UIElement content)
    {
        _banner.Visibility = content == _banner ? Visibility.Visible : Visibility.Collapsed;
        _hudCard.Visibility = content == _hudCard ? Visibility.Visible : Visibility.Collapsed;
        _volumeBanner.Visibility = content == _volumeBanner ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The volume was changed: show the level for a moment.</summary>
    void ShowVolume()
    {
        if (!Settings.VolumePopups || !CanShowHud) return;

        // Don't take a notification away while it's being read.
        bool showingCard = _mode == Mode.Hud && _hudCard.Visibility == Visibility.Visible;
        if (showingCard) return;

        // Glide the bar if the popup is already up; otherwise start at the right level.
        bool alreadyShowing = _mode == Mode.Hud && _volumeBanner.Visibility == Visibility.Visible;
        ShowHudContent(_volumeBanner);
        _volumeBanner.Set(_app.Volume.Level, _app.Volume.IsMuted, animate: alreadyShowing);
        ShowHud(VolumeHeight, TimeSpan.FromSeconds(1.6));
    }

    void ShowChargerBanner(bool plugged)
    {
        if (!Settings.PowerPopups) return;
        var power = _app.Power.State;

        string title = !plugged ? "On battery" : power.Percent >= 100 ? "Fully charged" : "Charging";
        string subtitle = plugged ? "Charger connected" : "Charger disconnected";
        ShowBanner(plugged ? "BoltGlyph" : "BatteryGlyph", power.Percent / 100.0, false,
                   title, subtitle, $"{power.Percent}%");
    }

    void ShowLowBatteryBanner()
    {
        if (!Settings.PowerPopups) return;
        var power = _app.Power.State;
        ShowBanner("BatteryGlyph", power.Percent / 100.0, true,
                   "Low battery", "Plug in your charger", $"{power.Percent}%");
    }

    void ShowTimerFinished()
    {
        SystemSounds.Asterisk.Play();
        int minutes = (int)Math.Round(_app.Timer.Duration.TotalMinutes);
        ShowBanner("CheckGlyph", 1, false, "Time's up", $"{minutes} minute focus session finished", "");
    }

    void ShowNotification(NotificationItem item)
    {
        if (!Settings.NotificationPopups || !CanShowHud) return;

        ShowHudContent(_hudCard);
        _hudCard.Show(item, enlarged: false);
        ShowHud(MeasureHudCard(), TimeSpan.FromSeconds(5));
    }

    void OnHudCardEnlargedChanged(NotificationCard card)
    {
        if (_mode != Mode.Hud) return;

        _hudHeight = MeasureHudCard();
        ApplyIsland(animate: true);

        // An enlarged card stays until the mouse leaves; a shrunk one resumes its countdown.
        if (card.IsEnlarged || Island.IsMouseOver)
            _hudTimer.Stop();
        else
            StartHudTimer(TimeSpan.FromSeconds(3));
    }

    double MeasureHudCard()
    {
        _hudCard.Measure(new Size(HudWidth, double.PositiveInfinity));
        return Math.Ceiling(_hudCard.DesiredSize.Height);
    }

    // ================= The closed pill =================

    /// <summary>
    /// Chooses what the pill shows. A running timer wins; then a lyric line, if lyrics
    /// are on; then the cover, clock and equalizer for music; otherwise battery and clock.
    /// </summary>
    void UpdatePill()
    {
        var timer = _app.Timer;
        var lyrics = _app.LyricsSession;
        bool showTimer = timer.IsRunning;
        bool showMedia = !showTimer && _np != null;
        bool lyricPill = showMedia && lyrics.HasSyncedLyrics;

        PillStatus.Visibility = lyricPill ? Visibility.Collapsed : Visibility.Visible;
        PillLyrics.Visibility = lyricPill ? Visibility.Visible : Visibility.Collapsed;
        CollapsedView.Width = lyricPill ? LyricPillWidth : Settings.CollapsedWidth;

        if (lyricPill != _lyricPill)
        {
            _lyricPill = lyricPill;
            if (_ready && _mode == Mode.Collapsed) ApplyIsland(animate: true);
        }
        UpdateGlow();

        if (lyricPill)
        {
            // Before the first line, and in instrumental gaps, show the song's title.
            string line = lyrics.LineText;
            PillLyricLine.SetText(line.Length > 0 ? line : _np!.Title, animate: _mode == Mode.Collapsed);
            PillBars.IsPlaying = false;
            return;
        }

        // Left slot: the cover while music shows, otherwise the ring.
        PillArt.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
        PillRing.Visibility = showMedia ? Visibility.Collapsed : Visibility.Visible;

        // Right slot: equalizer bars while music shows. On a narrow pill they replace the clock.
        bool roomForClock = !showMedia || Settings.CollapsedWidth >= 150;
        PillBars.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
        PillText.Visibility = roomForClock ? Visibility.Visible : Visibility.Collapsed;
        PillText.HorizontalAlignment = showMedia ? HorizontalAlignment.Center : HorizontalAlignment.Right;

        // No point animating the bars while the pill is faded out.
        PillBars.IsPlaying = showMedia && _np!.IsPlaying && _mode == Mode.Collapsed;

        if (showMedia)
        {
            var np = _np!;
            PillArt.Opacity = np.IsPlaying ? 1 : 0.55;
            if (!_pillArtShown || !ReferenceEquals(np.Art, _pillArt))
            {
                _pillArtShown = true;
                _pillArt = np.Art;
                PillArt.Background = np.Art != null
                    ? new ImageBrush(np.Art) { Stretch = Stretch.UniformToFill }
                    : (Brush)FindResource("PlaceholderBrush");
            }
            PillText.Text = DateTime.Now.ToString("t");
        }
        else if (showTimer)
        {
            PillRing.Value = timer.Remaining.TotalSeconds / timer.Duration.TotalSeconds;
            PillRing.SetWarning(false);
            PillText.Text = TimerPage.Format(timer.Remaining);
        }
        else
        {
            var power = _app.Power.State;
            PillRing.Value = power.HasBattery ? power.Percent / 100.0 : 1;
            PillRing.SetWarning(power.HasBattery && !power.IsPluggedIn && power.Percent <= 20);
            PillText.Text = DateTime.Now.ToString("t");
        }
    }

    // ================= Fullscreen apps =================

    /// <summary>A different window came to the foreground.</summary>
    void OnForegroundChanged()
    {
        // Not while the right-click menu is open: it must stay above the notch.
        if (Island.ContextMenu?.IsOpen != true)
            Native.BringToTop(_handle);
        CheckFullscreen();
    }

    void CheckFullscreen()
    {
        _fullscreenConfirm.Stop();
        bool fullscreen = Settings.HideInFullscreen && Native.IsFullscreenAppOnPrimary();
        bool hide = false;

        if (!fullscreen)
        {
            _fullscreenSince = null;
        }
        else
        {
            // Only hide once it has clearly settled in, and look again when that is due.
            _fullscreenSince ??= DateTime.UtcNow;
            hide = DateTime.UtcNow - _fullscreenSince >= FullscreenGrace;
            if (!hide) _fullscreenConfirm.Start();
        }

        if (hide == _hiddenForFullscreen) return;
        _hiddenForFullscreen = hide;

        if (hide) SetMode(Mode.Collapsed);

        // Fully transparent pixels also stop receiving clicks, so the notch is truly out of the way.
        Island.IsHitTestVisible = !hide;
        Motion.To(Island, OpacityProperty, hide ? 0 : 1, 220);
    }

    // ================= Right-click menu and settings =================

    void BuildContextMenu()
    {
        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => OpenSettings();

        var quit = new MenuItem { Header = "Quit Notch" };
        quit.Click += (_, _) => Application.Current.Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        menu.Closed += (_, _) =>
        {
            if (_mode == Mode.Expanded && !Island.IsMouseOver) Restart(_closeDelay);
        };

        Island.ContextMenu = menu;
    }

    void OpenSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_app.Settings);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
    }
}
