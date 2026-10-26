using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;

namespace JustVID;

// Native pixels are used for monitor bounds, including monitors with different DPI.
public sealed class WindowFrame
{
    readonly Window window;
    readonly WindowChrome chrome;
    IntPtr handle;
    Rect previousBounds;
    WindowState previousState;
    bool previousTopmost;
    public bool IsFullscreen { get; private set; }
    public WindowFrame(Window window)
    {
        this.window = window;
        chrome = WindowChrome.GetWindowChrome(window);
        window.SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(Hook);
            UpdateCorners();
        };
        window.StateChanged += (_, _) => UpdateCorners();
        window.SizeChanged += (_, _) => UpdateCorners();
    }
    public void EnterFullscreen()
    {
        if (IsFullscreen) return;
        previousState = window.WindowState;
        previousBounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
        previousTopmost = window.Topmost;
        IsFullscreen = true;
        window.WindowState = WindowState.Normal;
        WindowChrome.SetWindowChrome(window, null);
        window.ResizeMode = ResizeMode.NoResize;
        window.Topmost = true;
        ApplyMonitorBounds();
        UpdateCorners();
    }
    public void ExitFullscreen()
    {
        if (!IsFullscreen) return;
        IsFullscreen = false;
        window.Topmost = previousTopmost;
        WindowChrome.SetWindowChrome(window, chrome);
        window.ResizeMode = ResizeMode.CanResize;
        window.Left = previousBounds.Left; window.Top = previousBounds.Top;
        window.Width = previousBounds.Width; window.Height = previousBounds.Height;
        window.WindowState = previousState;
        UpdateCorners();
    }
    public void SnapToTop()
    {
        if (IsFullscreen || !GetCursorPos(out var cursor)) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(MonitorFromPoint(cursor, 2), ref info) &&
            cursor.Y <= info.Work.Top + Math.Max(2, (int)(4 * GetDpiForWindow(handle) / 96d)))
            window.WindowState = WindowState.Maximized;
    }
    void ApplyMonitorBounds()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return;
        var rect = info.Monitor; // Full monitor, not the taskbar-excluding work area.
        SetWindowPos(handle, window.Topmost ? new IntPtr(-1) : IntPtr.Zero, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0020 | 0x0040);
    }
    IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Borderless maximization must honor this monitor's work area, not its
        // full bounds. Fullscreen deliberately uses the separate path above.
        if (message == 0x0024 && !IsFullscreen) // WM_GETMINMAXINFO
        {
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor))
            {
                var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                limits.MaxPosition = new NativePoint(monitor.Work.Left - monitor.Monitor.Left, monitor.Work.Top - monitor.Monitor.Top);
                limits.MaxSize = new NativePoint(monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
                Marshal.StructureToPtr(limits, lParam, false);
                handled = true;
                return IntPtr.Zero;
            }
        }
        // WM_ACTIVATEAPP: release topmost when switching apps, restore when returning.
        if (message == 0x001C && IsFullscreen)
        {
            window.Topmost = wParam != IntPtr.Zero || previousTopmost;
            if (wParam != IntPtr.Zero) ApplyMonitorBounds();
        }
        if (message is 0x007E or 0x02E0)
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (IsFullscreen) ApplyMonitorBounds(); UpdateCorners(); }));
        return IntPtr.Zero;
    }
    void UpdateCorners()
    {
        if (handle == IntPtr.Zero) return;
        bool square = IsFullscreen || window.WindowState != WindowState.Normal;
        int preference = square ? 1 : 3; // DWMWCP_DONOTROUND / DWMWCP_ROUNDSMALL.
        int result = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        if (result == 0) return; // Windows 11 supplies small native corners and shadow.
        if (square) { SetWindowRgn(handle, IntPtr.Zero, true); return; }
        if (!GetWindowRect(handle, out var rect)) return;
        int diameter = (int)Math.Round(12 * GetDpiForWindow(handle) / 96d);
        var region = CreateRoundRectRgn(0, 0, rect.Right - rect.Left + 1, rect.Bottom - rect.Top + 1, diameter, diameter);
        if (SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
    }
    [StructLayout(LayoutKind.Sequential)] struct NativePoint
    {
        public int X, Y;
        public NativePoint(int x, int y) { X = x; Y = y; }
    }
    [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] public struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
}
