using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Notch.Interop;

/// <summary>The few Win32 calls WPF has no wrapper for.</summary>
static class Native
{
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x00000080;   // hidden from Alt+Tab
    const long WS_EX_NOACTIVATE = 0x08000000;   // clicking it never steals focus
    const long WS_EX_APPWINDOW = 0x00040000;

    /// <summary>Horizontal scroll: a tilt wheel, or a two-finger sideways swipe on a touchpad.</summary>
    public const int WM_MOUSEHWHEEL = 0x020E;

    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOACTIVATE = 0x0010;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint MONITORINFOF_PRIMARY = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
                               uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
                                         uint processId, uint threadId, uint flags);

    // Kept in a field so the garbage collector doesn't free the callback Windows is holding.
    static WinEventProc? _foregroundCallback;

    // Parts of Windows itself that cover the whole screen while they are up: Alt+Tab,
    // Task View, Start, Search, the notification centre. They aren't "fullscreen apps".
    static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "TaskSwitcherWnd",
        "ForegroundStaging", "Windows.UI.Core.CoreWindow", "LockScreenBackstopFrame",
    };

    // Processes whose fullscreen windows are overlays, not apps: Windows' own shell, and
    // desktop shells and launchers that replace or extend it. Their task switchers and
    // launchers cover the screen for a moment in just the same way Alt+Tab does.
    static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows
        "explorer", "ShellHost", "SearchHost", "SearchApp", "StartMenuExperienceHost",
        "ShellExperienceHost", "TextInputHost", "LockApp", "ScreenClippingHost",
        // Desktop shells, bars and window managers
        "seelen-ui", "komorebi", "komorebi-bar", "yasb", "zebar", "glazewm",
        // Launchers
        "Microsoft.CmdPal.UI", "PowerToys.PowerLauncher", "Flow.Launcher",
    };

    /// <summary>Turns a window into an overlay: no Alt+Tab entry, never takes focus.</summary>
    public static void MakeOverlay(IntPtr hwnd)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        ex &= ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>
    /// Puts the window back at the front of the always-on-top windows, without focusing
    /// it. Another always-on-top window that is used after ours would otherwise cover it.
    /// </summary>
    public static void BringToTop(IntPtr hwnd)
        => SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>
    /// Calls <paramref name="changed"/> on this thread whenever a different window comes
    /// to the foreground. Call once, from the UI thread.
    /// </summary>
    public static void WatchForeground(Action changed)
    {
        if (_foregroundCallback != null) return;
        _foregroundCallback = (_, _, _, _, _, _, _) => changed();
        SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                        _foregroundCallback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Asks Windows to draw a dark title bar. Ignored on builds that don't support it.</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        try
        {
            int enabled = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        }
        catch
        {
            // Cosmetic only.
        }
    }

    /// <summary>
    /// True when the focused window covers the whole primary monitor (a game, a video,
    /// a presentation). A maximised window doesn't count: its frame hangs past the screen
    /// edges, so its rectangle never equals the monitor's exactly.
    /// </summary>
    public static bool IsFullscreenAppOnPrimary()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == GetDesktopWindow() || hwnd == GetShellWindow())
                return false;

            if (!GetWindowRect(hwnd, out RECT window)) return false;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info)) return false;
            if ((info.dwFlags & MONITORINFOF_PRIMARY) == 0) return false;

            RECT monitor = info.rcMonitor;
            bool coversScreen = window.Left == monitor.Left && window.Top == monitor.Top
                             && window.Right == monitor.Right && window.Bottom == monitor.Bottom;

            return coversScreen && !IsShellWindow(hwnd);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True for Windows' own overlays (Alt+Tab, Start, the desktop and so on).</summary>
    static bool IsShellWindow(IntPtr hwnd)
    {
        var className = new StringBuilder(128);
        GetClassName(hwnd, className, className.Capacity);
        if (ShellWindowClasses.Contains(className.ToString())) return true;

        try
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            using var process = Process.GetProcessById((int)processId);
            return ShellProcesses.Contains(process.ProcessName);
        }
        catch
        {
            return false; // the process ended, or can't be inspected
        }
    }
}
