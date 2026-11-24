using System.Runtime.InteropServices;

namespace Notch.Interop;

/// <summary>Brightness of an external monitor over DDC/CI, for when Windows can't set it itself.</summary>
/// <remarks>Each call talks to the monitor and can take tens of milliseconds; call from a background thread.</remarks>
static class MonitorBrightness
{
    const uint MonitorDefaultToPrimary = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

    /// <summary>The primary screen's physical monitor, or zero when it doesn't answer DDC/CI.</summary>
    public static IntPtr OpenPrimary()
    {
        try
        {
            var monitor = MonitorFromPoint(new Point(), MonitorDefaultToPrimary);
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count == 0) return IntPtr.Zero;
            var monitors = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors)) return IntPtr.Zero;
            return Read(monitors[0].Handle) != null ? monitors[0].Handle : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>0 to 1, or null when the monitor didn't answer.</summary>
    public static double? Read(IntPtr monitor)
    {
        if (!GetMonitorBrightness(monitor, out uint min, out uint current, out uint max) || max <= min) return null;
        return (current - min) / (double)(max - min);
    }

    public static void Write(IntPtr monitor, double level)
    {
        if (!GetMonitorBrightness(monitor, out uint min, out _, out uint max) || max <= min) return;
        SetMonitorBrightness(monitor, (uint)Math.Round(min + level * (max - min)));
    }
}
