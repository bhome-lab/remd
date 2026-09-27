using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace RemoteControl.Infrastructure.Desktop;

public sealed class GdiDesktopScreen : IDesktopScreen
{
    public bool IsSupported => OperatingSystem.IsWindows();
    public bool IsInteractive => Environment.UserInteractive;
    public ScreenBounds PrimaryBounds => new(0, 0, Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));

    public Bitmap CaptureFrame(ScreenBounds bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(bounds.Width, bounds.Height));
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public WindowInfo[] EnumerateWindows()
    {
        var windows = new List<WindowInfo>();
        Native.EnumWindows((handle, _) =>
        {
            if (!Native.IsWindowVisible(handle)) return true;
            var length = Native.GetWindowTextLength(handle);
            if (length == 0) return true;
            var title = new StringBuilder(length + 1);
            Native.GetWindowText(handle, title, title.Capacity);
            windows.Add(new WindowInfo(handle.ToInt64(), title.ToString()));
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    public bool TryGetWindowBounds(long handle, out ScreenBounds bounds)
    {
        bounds = default;
        if (!Native.IsWindow((IntPtr)handle) || !Native.GetWindowRect((IntPtr)handle, out var rectangle)
            || rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top) return false;
        bounds = rectangle.ToBounds();
        return true;
    }

    public bool TryGetMonitorBounds(int index, out ScreenBounds bounds)
    {
        var monitors = new List<ScreenBounds>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr _, IntPtr _, ref Native.Rectangle rectangle, IntPtr _) =>
            {
                monitors.Add(rectangle.ToBounds());
                return true;
            }, IntPtr.Zero);
        bounds = index >= 0 && index < monitors.Count ? monitors[index] : default;
        return index >= 0 && index < monitors.Count;
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rectangle
        {
            public int Left, Top, Right, Bottom;
            public readonly ScreenBounds ToBounds() => new(Left, Top, Right - Left, Bottom - Top);
        }

        public delegate bool EnumWindowsCallback(IntPtr handle, IntPtr data);
        public delegate bool MonitorCallback(IntPtr monitor, IntPtr hdc, ref Rectangle bounds, IntPtr data);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr data);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rectangle rectangle);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorCallback callback, IntPtr data);
    }
}
