using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
        var handle = new WindowInteropHelper(window).Handle;
        GetWindowRect(handle, out var original);
        Invoke("ToggleFullscreen"); await Task.Delay(700);
        GetWindowRect(handle, out var actual);
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor);
        Check(actual.Equals(monitor.Monitor), "fullscreen covers entire monitor including taskbar area");
        var bottomWindow = WindowFromPoint(new NativePoint((actual.Left + actual.Right) / 2, actual.Bottom - 2));
        GetWindowThreadProcessId(bottomWindow, out uint process);
        Check(process == Environment.ProcessId, "taskbar does not cover fullscreen bottom edge");
        Invoke("ExitFullscreen"); await Task.Delay(500); GetWindowRect(handle, out actual);
        Check(Math.Abs(actual.Left - original.Left) <= 2 && Math.Abs(actual.Top - original.Top) <= 2 && Math.Abs(actual.Right - original.Right) <= 2 && Math.Abs(actual.Bottom - original.Bottom) <= 2, "fullscreen restores original bounds");
        Check(!ContainsGrip(window), "no resize-grip control");
        Console.WriteLine("Native Windows input and fullscreen smoke checks passed.");
    }
    static void Move(Point p) => SetCursorPos((int)p.X, (int)p.Y);
    static void RightClick(Grid input) { Move(input.PointToScreen(new Point(input.ActualWidth / 2, input.ActualHeight / 2))); mouse_event(8, 0, 0, 0, UIntPtr.Zero); mouse_event(16, 0, 0, 0, UIntPtr.Zero); }
    static bool ContainsGrip(DependencyObject root)
    {
        if (root is System.Windows.Controls.Primitives.ResizeGrip) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (ContainsGrip(VisualTreeHelper.GetChild(root, i))) return true;
        return false;
    }
    static void SaveOverlay(Grid input)
    {
        Directory.CreateDirectory("artifacts/ui");
        var image = new RenderTargetBitmap((int)input.ActualWidth, (int)input.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(input);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create("artifacts/ui/selection.png"); png.Save(stream);
    }
    [StructLayout(LayoutKind.Sequential)] readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
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
