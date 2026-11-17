using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Notch.Controls;
using Notch.Core;
using Notch.Interop;
using Notch.Services;

namespace Notch.Views;

/// <summary>
/// The notch itself. It is always in one of four modes:
/// <list type="bullet">
/// <item><b>Sleeping</b>: a thin strip tucked against the top edge, after a while untouched.</item>
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
    const double TabsIntentMs = 380;     // the pointer must rest on the page dots this long
    const double TabsIntentDrift = 10;   // and move less than this, before the icons open
    const double HiddenScale = 0.94;     // views shrink slightly while faded out
    const double HiddenBlur = 8;         // and blur, so they melt into the island rather than pop
    const double SleepWidth = 96;        // the strip the notch tucks into while asleep
    const double SleepHeight = 6;

    // A fullscreen window has to stay in front this long before the notch hides for it.
    // Switchers, launchers and screenshot overlays cover the screen only briefly.
    static readonly TimeSpan FullscreenGrace = TimeSpan.FromSeconds(1.2);

    enum Mode { Sleeping, Collapsed, Hud, Expanded }

    readonly AppServices _app;
    readonly MusicPage _music;
    readonly NotificationsPage _notifications;
    readonly TimerPage _timer;
    readonly DevicesPage _devices;
    readonly ShelfPage _shelf;
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
    readonly DispatcherTimer _tabsOpenDelay = new() { Interval = TimeSpan.FromMilliseconds(TabsIntentMs) };
    readonly DispatcherTimer _hudTimer = new();
    readonly DispatcherTimer _fullscreenCheck = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly DispatcherTimer _fullscreenConfirm = new() { Interval = FullscreenGrace + TimeSpan.FromMilliseconds(50) };
    readonly DispatcherTimer _sleepTimer = new();
    readonly DispatcherTimer _dragWatch = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer _morphEnd = new();

    Mode _mode = Mode.Collapsed;
    int _pageIndex;
    double _hudHeight = BannerHeight;
    bool _ready;
    bool _hiddenForFullscreen;
    DateTime? _fullscreenSince;

    bool _tabsOpen;
    Point _dotsHoverStart;  // where the pointer came onto the page dots
    bool _tabsPlaced;   // the icon row is already on screen, so move it rather than place it

    NowPlaying? _np;
    BitmapImage? _pillArt;
    bool _pillArtShown;
    bool _lyricPill;    // the closed notch is showing a lyric line

    bool _returnToSleep;    // the current popup woke the notch, so it goes back to sleep after

    // Drag and drop
    bool _dragHovering;     // files are being dragged over the notch
    DateTime? _dragLeftAt;  // when they last left it; brief exits happen between child elements
    bool _draggingOut;      // a file is being dragged out of the shelf

    // The morph between the pill and the open page
    readonly List<FrameworkElement> _morphHidden = new();

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
        _shelf = new ShelfPage(app);
        _allPages = new NotchPage[] { _music, _shelf, _notifications, _timer, _devices };
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
        Dots.MouseEnter += (_, e) => OnDotsHovered(e);
        Dots.MouseLeave += (_, _) =>
        {
            _tabsOpenDelay.Stop(); // passed over on the way somewhere else
            Restart(_tabsCloseDelay);
        };
        Tabs.Selected += GoToPage;
        Tabs.SettingsRequested += OpenSettings;
        Tabs.MouseEnter += (_, _) => _tabsCloseDelay.Stop();
        Tabs.MouseLeave += (_, _) => Restart(_tabsCloseDelay);

        _clock.Tick += (_, _) => UpdatePill();
        _openDelay.Tick += (_, _) => OnOpenDelayElapsed();
        _closeDelay.Tick += (_, _) => OnCloseDelayElapsed();
        _tabsCloseDelay.Tick += (_, _) => OnTabsCloseDelayElapsed();
        _tabsOpenDelay.Tick += (_, _) => OnTabsOpenDelayElapsed();
        _hudTimer.Tick += (_, _) => OnHudTimeout();
        _fullscreenCheck.Tick += (_, _) => OnForegroundChanged(); // a fallback for the foreground hook
        _fullscreenConfirm.Tick += (_, _) => CheckFullscreen();
        _sleepTimer.Tick += (_, _) => OnSleepTimerElapsed();
        _dragWatch.Tick += (_, _) => OnDragWatch();
        _morphEnd.Tick += (_, _) => EndMorph();

        _shelf.DraggingOutChanged += dragging =>
        {
            _draggingOut = dragging;
            _closeDelay.Stop();
            _dragWatch.Start(); // follows the pointer while WPF can't
        };

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
        UpdateSleepTimer();
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

        if (settings.SleepAfterSeconds <= 0 && _mode == Mode.Sleeping)
        {
            SetMode(Mode.Collapsed); // sleep was just switched off
            return;
        }

        ApplyIsland(animate: _ready);
        if (_ready) ApplyViews(animate: true);
        UpdateGlow();
        UpdateSleepTimer();
    }

    // ================= Modes =================

    void SetMode(Mode mode)
    {
        if (mode == _mode) return;

        var previous = _mode;
        if (_mode == Mode.Expanded) CurrentPage.OnHidden();
        _mode = mode;
        if (mode == Mode.Expanded) CurrentPage.OnShown();

        if (mode != Mode.Hud)
        {
            _hudTimer.Stop();
            _returnToSleep = false;
        }
        if (mode != Mode.Expanded)
        {
            _tabsOpen = false;
            _tabsCloseDelay.Stop();
            _tabsOpenDelay.Stop();
        }

        UpdatePill();
        ApplyIsland(animate: true);
        ApplyViews(animate: true);
        UpdateGlow();

        // The clock, cover and timer fly between the pill and the page.
        if (previous == Mode.Collapsed && mode == Mode.Expanded) StartMorph(opening: true);
        else if (previous == Mode.Expanded && mode == Mode.Collapsed) StartMorph(opening: false);
        else EndMorph();

        UpdateSleepTimer();
    }

    /// <summary>Sends the island towards the size the current mode asks for.</summary>
    void ApplyIsland(bool animate)
    {
        bool floating = Settings.Style == NotchStyle.Floating;
        double gap = floating ? Settings.TopGap : 0;
        var edge = floating ? IslandEdge.Floating : IslandEdge.Classic;
        double width, height, radius;

        switch (_mode)
        {
            case Mode.Sleeping:
                // Pulled up against the top of the screen in either style, out of the way.
                width = SleepWidth;
                height = SleepHeight;
                radius = SleepHeight / 2;
                edge = IslandEdge.Tucked;
                break;
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

        Island.Margin = new Thickness(0, gap, 0, 0);

        // The icon row appears in place, over the dots, and the island grows down around
        // it. Once it is showing it glides to follow page-height changes.
        double tabsY = height - TabsHeight - TabsBottom;
        if (animate && _tabsOpen && _tabsPlaced)
            Motion.To(_tabsShift, TranslateTransform.YProperty, tabsY, 420, Motion.Settle());
        else
            Motion.Set(_tabsShift, TranslateTransform.YProperty, tabsY);
        _tabsPlaced = _tabsOpen;

        IslandBody.MoveTo(width, height, radius, edge, gap, animate);
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
        // Already in that state: leave it alone. Re-running the fade on a view that is
        // showing would blur it and bring it back into focus for no reason (this used to
        // happen to the page whenever the page icons opened under it).
        bool wasShown = view.IsHitTestVisible;
        view.IsHitTestVisible = show;
        if (animate && wasShown == show) return;

        if (!animate)
        {
            view.Effect = null;
            Motion.Set(view, OpacityProperty, show ? 1 : 0);
            if (scale != null)
            {
                Motion.Set(scale, ScaleTransform.ScaleXProperty, show ? 1 : HiddenScale);
                Motion.Set(scale, ScaleTransform.ScaleYProperty, show ? 1 : HiddenScale);
            }
            return;
        }

        // Views come into focus as they fade in, and go soft as they fade out. The blur is
        // removed once a view is fully in, so text is drawn crisply again.
        if (view.Effect is not BlurEffect blur)
        {
            blur = new BlurEffect { Radius = show ? HiddenBlur : 0, RenderingBias = RenderingBias.Performance };
            view.Effect = blur;
        }

        if (show)
        {
            Motion.To(blur, BlurEffect.RadiusProperty, 0, 300, delayMs: 50, completed: () =>
            {
                if (view.IsHitTestVisible && view.Effect == blur) view.Effect = null;
            });

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
            Motion.To(blur, BlurEffect.RadiusProperty, HiddenBlur, 160);
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
        if (Settings.ShowShelfPage) wanted.Add(_shelf);
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

        Dots.SetCount(_pages.Count);
        Tabs.SetPages(_pages.Select(page => page.IconGlyph).ToList());
        ShowCurrentPageInstantly();
    }

    /// <summary>Shows the current page without the slide, for when the notch isn't open.</summary>
    void ShowCurrentPageInstantly()
    {
        foreach (var page in _allPages)
        {
            bool visible = _pages.Count > 0 && page == CurrentPage;
            page.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            page.IsHitTestVisible = visible;
            Motion.Set(page, OpacityProperty, 1);
            Motion.Set((TranslateTransform)page.RenderTransform, TranslateTransform.XProperty, 0);
        }

        Dots.SetActive(_pageIndex, animate: false);
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

    /// <summary>
    /// The pointer reached the page dots. The icons open only if it rests there for a
    /// moment, so sweeping down out of the notch, or brushing the bottom edge while using
    /// a page, doesn't open them by accident.
    /// </summary>
    void OnDotsHovered(MouseEventArgs e)
    {
        if (!Settings.PageIcons || _mode != Mode.Expanded) return;
        _tabsCloseDelay.Stop();
        if (_tabsOpen) return;

        _dotsHoverStart = e.GetPosition(Island);
        Restart(_tabsOpenDelay);
    }

    void OnTabsOpenDelayElapsed()
    {
        _tabsOpenDelay.Stop();
        if (_tabsOpen || _mode != Mode.Expanded || !IsCursorOver(Dots)) return;

        // Still drifting across: give it another moment and measure from here.
        var now = Mouse.GetPosition(Island);
        if ((now - _dotsHoverStart).Length > TabsIntentDrift)
        {
            _dotsHoverStart = now;
            Restart(_tabsOpenDelay);
            return;
        }

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
        _sleepTimer.Stop();

        if (_mode == Mode.Hud)
            _hudTimer.Stop(); // keep the popup up while it's being looked at
        else if (_mode == Mode.Sleeping && Settings.Open == OpenMode.Click)
            SetMode(Mode.Collapsed); // wake up to the pill; a click opens it
        else if ((_mode == Mode.Collapsed || _mode == Mode.Sleeping) && Settings.Open == OpenMode.Hover)
            Restart(_openDelay); // a short wait, so brushing past the top edge doesn't open it
    }

    void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_mode == Mode.Expanded)
            Restart(_closeDelay);
        else if (_mode == Mode.Hud)
            StartHudTimer(TimeSpan.FromSeconds(1.2));
        else if (_mode == Mode.Collapsed)
            UpdateSleepTimer();
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
        else if (_mode == Mode.Collapsed || _mode == Mode.Sleeping || banner)
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

        // The sleeping strip is only a few pixels tall, and in floating style the pill sits
        // below the screen edge, so check the real pointer (the gap above the island counts).
        if ((_mode == Mode.Collapsed || _mode == Mode.Sleeping) && IsCursorOverIsland())
            SetMode(Mode.Expanded);
        else if (_mode == Mode.Collapsed)
            UpdateSleepTimer();
    }

    void OnCloseDelayElapsed()
    {
        _closeDelay.Stop();
        bool menuOpen = Island.ContextMenu?.IsOpen == true;
        if (_mode != Mode.Expanded || menuOpen || _draggingOut || _dragHovering) return;

        if (IsCursorOverIsland())
        {
            // Pushed up into the gap above the island, where WPF can't see the pointer:
            // stay open, and look again shortly.
            if (!Island.IsMouseOver) Restart(_closeDelay);
            return;
        }

        SetMode(Mode.Collapsed);
    }

    /// <summary>
    /// Whether the pointer is over the island, or in the gap straight above it. Asks Windows
    /// rather than WPF, which loses track during drag and drop and over transparent pixels.
    /// </summary>
    bool IsCursorOverIsland(double slack = 2)
    {
        if (!Native.TryGetCursorPos(out var screen) || PresentationSource.FromVisual(IslandBody) == null)
            return Island.IsMouseOver;

        Point point;
        try
        {
            point = IslandBody.PointFromScreen(screen);
        }
        catch (InvalidOperationException)
        {
            return Island.IsMouseOver;
        }

        var body = IslandBody.Outline.Rect;
        if (body.IsEmpty) return false;

        double screenTop = -Island.Margin.Top - 1;
        double top = Math.Min(body.Top, screenTop);
        var area = new Rect(body.Left - slack, top, body.Width + 2 * slack, body.Bottom - top + slack);
        return area.Contains(point);
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
        {
            ApplyIsland(animate: true); // already showing one: just resize
        }
        else
        {
            bool wasAsleep = _mode == Mode.Sleeping;
            SetMode(Mode.Hud);
            _returnToSleep = wasAsleep;
        }

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
        if (_mode != Mode.Hud) return;

        // A popup that woke the notch puts it back to sleep, unless the pointer is on it.
        bool sleep = _returnToSleep && Settings.SleepAfterSeconds > 0 && !IsCursorOverIsland();
        SetMode(sleep ? Mode.Sleeping : Mode.Collapsed);
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
            PillRing.Glyph = null;
            PillText.Text = TimerPage.Format(timer.Remaining);
        }
        else
        {
            var power = _app.Power.State;
            PillRing.Value = power.HasBattery ? power.Percent / 100.0 : 1;
            PillRing.SetWarning(power.HasBattery && !power.IsPluggedIn && power.Percent <= 20);
            PillRing.Glyph = power.HasBattery && power.IsPluggedIn ? (Geometry)FindResource("BoltGlyph") : null;
            PillText.Text = DateTime.Now.ToString("t");
        }
    }

    // ================= Sleep =================

    /// <summary>Starts the countdown to sleep while the pill is showing; stops it otherwise.</summary>
    void UpdateSleepTimer()
    {
        _sleepTimer.Stop();
        int seconds = Settings.SleepAfterSeconds;
        if (!_ready || seconds <= 0 || _mode != Mode.Collapsed) return;

        _sleepTimer.Interval = TimeSpan.FromSeconds(seconds);
        _sleepTimer.Start();
    }

    void OnSleepTimerElapsed()
    {
        _sleepTimer.Stop();
        if (_mode != Mode.Collapsed) return;

        // Not while it's being pointed at or something is being dragged onto it.
        if (_dragHovering || _draggingOut || IsCursorOverIsland())
        {
            UpdateSleepTimer();
            return;
        }

        SetMode(Mode.Sleeping);
    }

    // ================= Drag and drop: the file shelf =================

    bool CanTakeDrop(DragEventArgs e)
        => Settings.ShowShelfPage && !_draggingOut && !_hiddenForFullscreen
        && e.Data.GetDataPresent(DataFormats.FileDrop);

    // Also handles DragEnter.
    void Island_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!CanTakeDrop(e))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        // Only paths are kept, so the files themselves are never touched.
        e.Effects = DragDropEffects.Copy;
        _dragLeftAt = null;
        if (!_dragHovering) BeginDragHover();
    }

    void Island_DragLeave(object sender, DragEventArgs e)
    {
        // Moving between elements inside the notch also raises this, followed at once by
        // another DragOver. The drag watch decides whether it really left.
        e.Handled = true;
        if (_dragHovering) _dragLeftAt = DateTime.UtcNow;
    }

    void Island_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (CanTakeDrop(e) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _app.Shelf.Add(paths);
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        EndDragHover();
    }

    /// <summary>Files arrived over the notch: wake up and open on the shelf, ready for the drop.</summary>
    void BeginDragHover()
    {
        _dragHovering = true;
        _openDelay.Stop();
        _closeDelay.Stop();
        _sleepTimer.Stop();

        int index = _pages.IndexOf(_shelf);
        if (index >= 0)
        {
            if (_mode == Mode.Expanded)
            {
                GoToPage(index);
            }
            else
            {
                _pageIndex = index;
                ShowCurrentPageInstantly();
                SetMode(Mode.Expanded);
            }
        }

        _shelf.SetDropHighlight(true);
        _dragWatch.Start();
    }

    void EndDragHover()
    {
        _dragHovering = false;
        _dragLeftAt = null;
        _shelf.SetDropHighlight(false);
    }

    /// <summary>
    /// Runs during and just after a drag, when WPF doesn't see the mouse: ends a drag that
    /// left the notch, gets the open notch out of the way of a file being dragged out, and
    /// closes it once the pointer has moved away afterwards.
    /// </summary>
    void OnDragWatch()
    {
        bool over = IsCursorOverIsland(slack: 6);

        if (_dragHovering)
        {
            bool left = _dragLeftAt is { } at && DateTime.UtcNow - at > TimeSpan.FromMilliseconds(150);
            if (!left) return;
            EndDragHover(); // dragged away, or the drag was cancelled
        }

        if (_draggingOut)
        {
            // Close as soon as the file leaves the notch, so whatever is beneath can take it.
            if (!over && _mode == Mode.Expanded) SetMode(Mode.Collapsed);
            return;
        }

        if (over) return;
        _dragWatch.Stop();

        if (_mode == Mode.Expanded) Restart(_closeDelay);
        else UpdateSleepTimer();
    }

    // ================= Morph between the pill and the page =================

    /// <summary>The pill's elements that can fly into a page, keyed by <see cref="MorphKeys"/>.</summary>
    IEnumerable<(string Key, FrameworkElement Element)> PillMorphSources()
    {
        if (_lyricPill) yield break;

        bool timer = _app.Timer.IsRunning;
        if (PillText.IsVisible) yield return (timer ? MorphKeys.Countdown : MorphKeys.Clock, PillText);
        if (timer && PillRing.IsVisible) yield return (MorphKeys.CountdownRing, PillRing);
        if (PillArt.IsVisible) yield return (MorphKeys.Art, PillArt);
    }

    /// <summary>
    /// Flies the pill's clock, cover or timer into the same thing on the open page, or back.
    /// Each pair is drawn twice along the same path, the old look fading into the new, while
    /// the real elements are hidden until it lands.
    /// </summary>
    void StartMorph(bool opening)
    {
        EndMorph();
        if (!_ready || _pages.Count == 0) return;

        ContentHost.UpdateLayout(); // the pill and page may have just changed what they show
        var page = CurrentPage;
        double ms = opening ? 620 : 420;

        foreach (var (key, pillElement) in PillMorphSources())
        {
            if (page.MorphTarget(key) is not { IsVisible: true } pageElement) continue;
            if (!TryRestingBounds(pillElement, out var pillRect) || !TryRestingBounds(pageElement, out var pageRect))
                continue;

            var (from, fromRect, to, toRect) = opening
                ? (pillElement, pillRect, pageElement, pageRect)
                : (pageElement, pageRect, pillElement, pillRect);

            if (CopyOf(from) is not { } fromGhost || CopyOf(to) is not { } toGhost) continue;

            var ease = opening ? Motion.Spring() : Motion.Settle();
            Fly(fromGhost, fromRect, toRect, from.Opacity, 0, ms, ease, fadeMs: ms * 0.4);
            Fly(toGhost, fromRect, toRect, 0, to.Opacity, ms, ease, fadeMs: ms * 0.5);

            Hide(from);
            Hide(to);
        }

        if (MorphLayer.Children.Count == 0) return;
        _morphEnd.Interval = TimeSpan.FromMilliseconds((ms + 30) * Motion.SpeedFactor);
        _morphEnd.Start();
    }

    /// <summary>Lands the morph straight away: removes the copies and shows the real elements.</summary>
    void EndMorph()
    {
        _morphEnd.Stop();
        MorphLayer.Children.Clear();
        foreach (var element in _morphHidden) element.OpacityMask = null;
        _morphHidden.Clear();
    }

    void Hide(FrameworkElement element)
    {
        // An opacity mask hides it without touching Opacity or Visibility, which the pill
        // and pages set themselves.
        element.OpacityMask = Brushes.Transparent;
        _morphHidden.Add(element);
    }

    /// <summary>
    /// Moves a copy from one rectangle to another, scaled evenly by height and kept centred,
    /// while its opacity goes from <paramref name="fromOpacity"/> to <paramref name="toOpacity"/>.
    /// </summary>
    void Fly(FrameworkElement ghost, Rect fromRect, Rect toRect, double fromOpacity, double toOpacity,
             double ms, IEasingFunction ease, double fadeMs)
    {
        double width = ghost.Width, height = ghost.Height;
        var scale = new ScaleTransform();
        var shift = new TranslateTransform();
        var transforms = new TransformGroup();
        transforms.Children.Add(scale);
        transforms.Children.Add(shift);
        ghost.RenderTransform = transforms;
        MorphLayer.Children.Add(ghost);

        void Place(Rect rect, out double s, out double x, out double y)
        {
            s = rect.Height / height;
            x = rect.X + rect.Width / 2 - s * width / 2;
            y = rect.Y + rect.Height / 2 - s * height / 2;
        }

        Place(fromRect, out double s0, out double x0, out double y0);
        Place(toRect, out double s1, out double x1, out double y1);

        Motion.Set(scale, ScaleTransform.ScaleXProperty, s0);
        Motion.Set(scale, ScaleTransform.ScaleYProperty, s0);
        Motion.Set(shift, TranslateTransform.XProperty, x0);
        Motion.Set(shift, TranslateTransform.YProperty, y0);
        Motion.Set(ghost, OpacityProperty, fromOpacity);

        Motion.To(scale, ScaleTransform.ScaleXProperty, s1, ms, ease);
        Motion.To(scale, ScaleTransform.ScaleYProperty, s1, ms, ease);
        Motion.To(shift, TranslateTransform.XProperty, x1, ms, ease);
        Motion.To(shift, TranslateTransform.YProperty, y1, ms, ease);
        Motion.To(ghost, OpacityProperty, toOpacity, fadeMs);
    }

    /// <summary>
    /// Where an element sits in ContentHost once its view is fully shown: the views' own
    /// fade-in scale is left out, since it's still running when this is measured.
    /// </summary>
    bool TryRestingBounds(FrameworkElement element, out Rect rect)
    {
        rect = Rect.Empty;
        try
        {
            FrameworkElement view = element.IsDescendantOf(ExpandedView) ? ExpandedView : CollapsedView;
            rect = element.TransformToAncestor(view).TransformBounds(new Rect(element.RenderSize));
            rect.Offset(VisualTreeHelper.GetOffset(view));
            return rect.Width >= 1 && rect.Height >= 1;
        }
        catch (InvalidOperationException)
        {
            return false; // not in the tree right now
        }
    }

    /// <summary>A look-alike of a text, cover or ring, to fly in its place.</summary>
    static FrameworkElement? CopyOf(FrameworkElement element)
    {
        double width = element.ActualWidth, height = element.ActualHeight;
        if (width < 1 || height < 1) return null;

        switch (element)
        {
            case TextBlock text:
                var copy = new TextBlock
                {
                    Text = text.Text,
                    FontFamily = text.FontFamily,
                    FontSize = text.FontSize,
                    FontWeight = text.FontWeight,
                    Foreground = text.Foreground,
                    Width = width,
                    Height = height,
                };
                System.Windows.Documents.Typography.SetNumeralAlignment(
                    copy, System.Windows.Documents.Typography.GetNumeralAlignment(text));
                return copy;

            case Border border:
                return new Border
                {
                    Width = width,
                    Height = height,
                    CornerRadius = border.CornerRadius,
                    Background = border.Background,
                };

            case ProgressRing ring:
                return new ProgressRing
                {
                    Width = width,
                    Height = height,
                    StrokeWidth = ring.StrokeWidth,
                    Value = ring.Value,
                    Stroke = ring.Stroke,
                    Glyph = ring.Glyph,
                };

            default:
                return null;
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
