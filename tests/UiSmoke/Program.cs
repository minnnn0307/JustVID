using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JustVID;

static class Smoke
{
    static MainWindow window = null!;
    static readonly BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, fields)!.GetValue(window)!;
    static void Invoke(string name) => typeof(MainWindow).GetMethod(name, fields)!.Invoke(window, null);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
    [STAThread] public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            int result = 0;
            try { window = new MainWindow(); window.Show(); await Run(); }
            catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
            finally { keybd_event(0x11, 0, 2, UIntPtr.Zero); mouse_event(0x0004 | 0x0010, 0, 0, 0, UIntPtr.Zero); window?.Close(); app.Shutdown(result); }
        };
        Environment.ExitCode = app.Run();
    }
    static async Task Wait(Func<bool> condition, string message)
    {
        for (int i = 0; i < 150; i++) { if (condition()) return; await Task.Delay(100); }
        throw new Exception(message);
    }
    static async Task Run()
    {
        await Task.Delay(500);
        var logo = Field<Viewbox>("EmptyLogo");
        Check(logo.IsVisible && !logo.IsHitTestVisible, "idle screen shows a non-clickable centered JustVID logo");
        Check(typeof(MainWindow).GetField("OpenHint", fields) == null, "idle file-open button removed");
        SaveElement(Field<Grid>("RootLayout"), "idle-logo");
        typeof(MainWindow).GetMethod("OpenFile", fields)!.Invoke(window, new object[] { Path.Combine(AppContext.BaseDirectory, "fixture.mp4") });
        await Wait(() => Field<bool>("ready"), "Fixture did not decode");
        await Task.Delay(500);
        var input = Field<Grid>("InputArea");
        var selection = Field<Border>("Selection");
        var view = Field<VideoEngineView>("Player");
        var overlay = Window.GetWindow(input);
        overlay.Activate(); SetForegroundWindow(new WindowInteropHelper(overlay).Handle);
        var start = input.PointToScreen(new Point(input.ActualWidth * .25, input.ActualHeight * .25));
        var end = input.PointToScreen(new Point(input.ActualWidth * .65, input.ActualHeight * .65));
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        Move(start); mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(150);
        Move(end); await Task.Delay(250);
        Check(selection.Visibility == Visibility.Visible && selection.Width > 10 && selection.Height > 10, "native Ctrl drag shows selection overlay");
        SaveOverlay(input);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(300);
        var crop = Field<Region>("crop");
        Check(crop.Width < .6 && crop.Height < .6 && crop.X > .1, "Ctrl drag applies selected source region");
        Check(selection.Visibility == Visibility.Collapsed, "selection closes after applying crop");
        var player = typeof(VideoEngineView).GetField("playback", fields)!.GetValue(view)!;
        Check(!string.IsNullOrEmpty((string?)player.GetType().GetProperty("CropGeometry")!.GetValue(player)), "LibVLC receives crop geometry");
        RightClick(input); await Task.Delay(300);
        Check(Field<Region>("crop") == new Region(0, 0, 1, 1), "Ctrl right click restores original region");
        Check(string.IsNullOrEmpty((string?)player.GetType().GetProperty("CropGeometry")!.GetValue(player)), "LibVLC crop resets");
        var tiny = input.PointToScreen(new Point(input.ActualWidth * .5, input.ActualHeight * .5));
        Move(tiny); mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
        Move(new Point(tiny.X + 1, tiny.Y + 1)); await Task.Delay(100); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(200);
        Check(Field<Region>("crop").Width < .02, "one-screen-pixel drag zooms");
        RightClick(input); keybd_event(0x11, 0, 2, UIntPtr.Zero); await Task.Delay(200);
        await VerifyCropPixels(input, .15, .12, .70, .16, "wide");
        await VerifyCropPixels(input, .40, .12, .10, .70, "tall");
        var handle = new WindowInteropHelper(window).Handle;
        GetWindowRect(handle, out var original);
        Invoke("ToggleFullscreen"); await Task.Delay(700);
        GetWindowRect(handle, out var actual);
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor);
        Check(actual.Equals(monitor.Monitor), "fullscreen covers entire monitor including taskbar area");
        var controls = Field<Popup>("FullscreenControls");
        Check(!controls.IsOpen && Math.Abs(Field<Grid>("VideoArea").ActualHeight - window.ActualHeight) < 2,
            "fullscreen starts with controls hidden and no reserved control row");
        var bottomWindow = WindowFromPoint(new NativePoint((actual.Left + actual.Right) / 2, actual.Bottom - 2));
        GetWindowThreadProcessId(bottomWindow, out uint process);
        Check(process == Environment.ProcessId, "taskbar does not cover fullscreen bottom edge");
        var videoHeight = Field<Grid>("VideoArea").ActualHeight;
        Move(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight / 2))); await Task.Delay(200);
        Move(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight - 20)));
        await Wait(() => controls.IsOpen, "bottom edge did not reveal fullscreen controls");
        Check(controls.IsOpen && Math.Abs(Field<Grid>("VideoArea").ActualHeight - videoHeight) < 2,
            "bottom mouse movement reveals controls without resizing video");
        var timeline = Field<Slider>("Timeline");
        await Task.Delay(200);
        Move(timeline.PointToScreen(new Point(timeline.ActualWidth * .2, timeline.ActualHeight / 2)));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        await Wait(() => view.Position.TotalSeconds > 4 && view.Position.TotalSeconds < 9, "fullscreen seek controls did not receive native click");
        Check(true, "fullscreen overlay timeline receives native seek click");
        Move(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight / 2)));
        await Task.Delay(250);
        Check(!controls.IsOpen, "controls hide immediately when mouse leaves their area");
        Move(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight - 20)));
        await Wait(() => controls.IsOpen, "controls did not reappear after returning to bottom");
        await Wait(() => !controls.IsOpen, "fullscreen controls did not hide after inactivity");
        Check(!controls.IsOpen, "fullscreen controls auto-hide after inactivity");
        keybd_event(0x1B, 0, 0, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero); await Task.Delay(150);
        Check(Field<bool>("fullscreen"), "Escape no longer changes window mode");
        SendEnter(); await Task.Delay(500); GetWindowRect(handle, out actual);
        Check(!Field<bool>("fullscreen") && Field<Grid>("ControlBar").IsVisible, "Enter exits fullscreen and restores normal controls");
        Check(Math.Abs(actual.Left - original.Left) <= 2 && Math.Abs(actual.Top - original.Top) <= 2 && Math.Abs(actual.Right - original.Right) <= 2 && Math.Abs(actual.Bottom - original.Bottom) <= 2, "fullscreen restores original bounds");
        SendEnter(); await Task.Delay(300);
        Check(Field<bool>("fullscreen"), "Enter in normal window restores previous fullscreen mode");
        Move(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight / 2))); await Task.Delay(150);
        Move(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight - 20)));
        await Wait(() => controls.IsOpen, "controls not revealed for popup Enter check");
        await Task.Delay(150);
        Move(timeline.PointToScreen(new Point(timeline.ActualWidth * .2, timeline.ActualHeight / 2)));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        SendEnter(); await Task.Delay(300);
        Check(!Field<bool>("fullscreen") && window.WindowState == WindowState.Normal, "Enter works while fullscreen controls have focus");
        window.WindowState = WindowState.Maximized; await Task.Delay(500);
        GetWindowRect(handle, out actual);
        GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor);
        Check(actual.Left >= monitor.Work.Left && actual.Top >= monitor.Work.Top && actual.Right <= monitor.Work.Right && actual.Bottom <= monitor.Work.Bottom,
            "maximize bounds stay inside monitor work area");
        var controlsBottom = window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight - 2));
        Check(controlsBottom.Y < monitor.Work.Bottom && controlsBottom.Y >= monitor.Work.Top, "maximized bottom controls remain above taskbar");
        Invoke("ToggleFullscreen"); await Task.Delay(500); Invoke("ExitFullscreen"); await Task.Delay(500);
        GetWindowRect(handle, out actual);
        Check(window.WindowState == WindowState.Maximized && actual.Bottom <= monitor.Work.Bottom, "fullscreen from maximized restores work-area bounds");
        window.Activate(); SendEnter(); await Task.Delay(300);
        Check(window.WindowState == WindowState.Normal, "Enter restores a maximized window");
        SendEnter(); await Task.Delay(300);
        Check(!Field<bool>("fullscreen") && window.WindowState == WindowState.Maximized, "Enter restores previous maximized mode instead of fullscreen");
        SendEnter(); await Task.Delay(300);
        Check(window.WindowState == WindowState.Normal, "Enter toggles maximized mode back to normal repeatedly");
        window.WindowState = WindowState.Maximized; await Task.Delay(300); Invoke("ToggleFullscreen"); await Task.Delay(300);
        SendEnter(); await Task.Delay(500);
        Check(!Field<bool>("fullscreen") && window.WindowState == WindowState.Normal, "Enter from fullscreen entered while maximized restores normal window");
        SendEnter(); await Task.Delay(300);
        Check(Field<bool>("fullscreen"), "fullscreen entered from maximized is remembered as fullscreen");
        SendEnter(); await Task.Delay(300);
        Check(!Field<bool>("fullscreen") && window.WindowState == WindowState.Normal, "restored fullscreen toggles back to normal");
        Check(!ContainsGrip(window), "no resize-grip control");
        Console.WriteLine("Native Windows input and fullscreen smoke checks passed.");
    }
    static async Task VerifyCropPixels(Grid input, double x, double y, double width, double height, string name)
    {
        // Compare actual screen pixels, not just the crop string returned by VLC.
        var a = input.PointToScreen(new Point(input.ActualWidth * x, input.ActualHeight * y));
        var b = input.PointToScreen(new Point(input.ActualWidth * (x + width), input.ActualHeight * (y + height)));
        var localA = input.PointFromScreen(new Point((int)a.X, (int)a.Y));
        var localB = input.PointFromScreen(new Point((int)b.X, (int)b.Y));
        var view = Field<VideoEngineView>("Player");
        var proposed = ZoomMath.Select(new Region(0, 0, 1, 1), localA.X, localA.Y, localB.X, localB.Y, input.ActualWidth, input.ActualHeight);
        var px = ZoomMath.Pixels(proposed, view.NaturalVideoWidth, view.NaturalVideoHeight);
        double[] samples = [.17, .47, .77];
        var expected = (from sy in samples from sx in samples
            select ScreenPixel(input.PointToScreen(new Point(
                (px.X + px.Width * sx) / view.NaturalVideoWidth * input.ActualWidth,
                (px.Y + px.Height * sy) / view.NaturalVideoHeight * input.ActualHeight)))).ToArray();
        Check(expected.Distinct().Count() > 1 && !expected.Contains(uint.MaxValue), name + " baseline has visible decoded colors");
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        Move(a); mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
        Move(b); await Task.Delay(100); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        Move(window.PointToScreen(new Point(30, 20)));
        bool Match()
        {
            var actual = (from sy in samples from sx in samples select ScreenPixel(input.PointToScreen(new Point(input.ActualWidth * sx, input.ActualHeight * sy)))).ToArray();
            return actual.Zip(expected).All(pair => ColorDistance(pair.First, pair.Second) < 55);
        }
        await Wait(Match, name + " crop did not render the selected original pixels");
        Check(Match(), name + " crop renders selected source colors without distortion");
        var crop = Field<Region>("crop");
        Check(Math.Abs(view.ActualWidth / view.ActualHeight - view.NaturalVideoWidth * crop.Width / (view.NaturalVideoHeight * crop.Height)) < .03,
            name + " viewport preserves selected aspect ratio");
        RightClick(input); keybd_event(0x11, 0, 2, UIntPtr.Zero); await Task.Delay(500);
    }
    static int ColorDistance(uint a, uint b) => Math.Abs((int)(a & 255) - (int)(b & 255)) + Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255)) + Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255));
    static uint ScreenPixel(Point point)
    {
        var dc = GetDC(IntPtr.Zero);
        try { return GetPixel(dc, (int)point.X, (int)point.Y); }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }
    static void Move(Point p) => SetCursorPos((int)p.X, (int)p.Y);
    static void RightClick(Grid input) { Move(input.PointToScreen(new Point(input.ActualWidth / 2, input.ActualHeight / 2))); mouse_event(8, 0, 0, 0, UIntPtr.Zero); mouse_event(16, 0, 0, 0, UIntPtr.Zero); }
    static bool ContainsGrip(DependencyObject root)
    {
        if (root is System.Windows.Controls.Primitives.ResizeGrip) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (ContainsGrip(VisualTreeHelper.GetChild(root, i))) return true;
        return false;
    }
    static void SendEnter() { keybd_event(0x0D, 0, 0, UIntPtr.Zero); keybd_event(0x0D, 0, 2, UIntPtr.Zero); }
    static void SaveOverlay(Grid input) => SaveElement(input, "selection");
    static void SaveElement(FrameworkElement element, string name)
    {
        Directory.CreateDirectory("artifacts/ui");
        var image = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create($"artifacts/ui/{name}.png"); png.Save(stream);
    }
    [StructLayout(LayoutKind.Sequential)] readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
}
