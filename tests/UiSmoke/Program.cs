using System.IO;
using System.Reflection;
using System.Diagnostics;
using LibVLCSharp.Shared;
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;
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
        var caption = new[] { Field<Button>("MinimizeButton"), Field<Button>("MaximizeButton"), Field<Button>("CloseButton") };
        Check(caption.All(b => Math.Abs(b.ActualWidth - caption[0].ActualWidth) < .1 && Math.Abs(b.ActualHeight - caption[0].ActualHeight) < .1), "caption buttons have equal hit target dimensions");
        Click(Field<Button>("SettingsButton"));
        await Wait(() => Field<Popup>("SettingsPopup").IsOpen, "settings popup did not open");
        await Task.Delay(300);
        SaveElement(Field<Grid>("SettingsBackdrop"), "settings-open");
        Click(Field<Button>("LightThemeChoice"));
        await Wait(() => Field<bool>("light"), "light theme click did not apply");
        Check(Field<bool>("light"), "light theme can be selected in settings");
        Check(((SolidColorBrush)window.Resources["ControlInk"]).Color == (Color)ColorConverter.ConvertFromString("#C5E6A7"), "light playback icons and slider thumbs use pale lime ink");
        SaveElement(Field<Grid>("SettingsBackdrop"), "settings-light");
        Click(Field<Button>("InfoTab")); await Task.Delay(150);
        Check(Field<TextBlock>("InfoEmpty").IsVisible, "media information shows an empty state before opening a video");
        SendEscape(); await Task.Delay(150);
        Check(!Field<Popup>("SettingsPopup").IsOpen, "Escape closes only the settings popup");
        SaveElement(Field<Grid>("RootLayout"), "idle-light");
        typeof(MainWindow).GetMethod("OpenFile", fields)!.Invoke(window, new object[] { Path.Combine(AppContext.BaseDirectory, "fixture.mp4") });
        await Wait(() => Field<bool>("ready"), "Fixture did not decode");
        await Task.Delay(500);
        var input = Field<Grid>("InputArea");
        var selection = Field<Border>("Selection");
        var view = Field<VideoEngineView>("Player");
        Click(Field<Button>("SettingsButton")); await Task.Delay(150);
        Click(Field<Button>("InfoTab")); await Task.Delay(150);
        var information = Field<ItemsControl>("InfoSections").Items.OfType<InformationSection>().ToArray();
        Check(information.Any(section => section.Rows.Any(row => row.Value == "320 × 180")), "media information reads actual source resolution");
        Check(information.Any(section => section.Rows.Any(row => row.Value.Contains("H.264"))), "media information reads the video codec");
        Check(information.Any(section => section.Rows.Any(row => row.Value.Contains("2 fps"))), "media information reads actual frame rate");
        SaveElement(Field<Grid>("SettingsBackdrop"), "settings-media-info");
        Click(Field<Button>("AppearanceTab")); await Task.Delay(100);
        Click(Field<Button>("DarkThemeChoice")); await Task.Delay(100);
        Check(!Field<bool>("light"), "dark theme can be selected in settings");
        SaveElement(Field<Grid>("SettingsBackdrop"), "settings-dark");
        SendEscape(); await Task.Delay(150);
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
        await Task.Delay(2800);
        Check(controls.IsOpen, "fullscreen controls remain visible while pointer rests inside");
        Move(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight - Field<Grid>("ControlBar").ActualHeight - Field<Grid>("ControlBar").Margin.Top - Field<Grid>("ControlBar").Margin.Bottom - 6)));
        await Task.Delay(250);
        Check(controls.IsOpen, "fullscreen control edge allowance prevents premature hiding");
        Move(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight / 2)));
        await Task.Delay(250);
        Check(!controls.IsOpen, "fullscreen controls hide only after leaving the full interaction region");
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
        typeof(MainWindow).GetMethod("OpenFile", fields)!.Invoke(window, new object[] { Path.Combine(AppContext.BaseDirectory, "audio-fixture.mp4") });
        await Wait(() => Field<bool>("ready"), "subtitle fixture did not open");
        Click(Field<Button>("SettingsButton")); await Task.Delay(300);
        Click(Field<Button>("SubtitleTab")); await Task.Delay(200);
        Field<Slider>("SubtitleSize").Value = 100; Field<Slider>("SubtitleMargin").Value = 0;
        typeof(MainWindow).GetField("subtitleFile", fields)!.SetValue(window, Path.Combine(AppContext.BaseDirectory, "test-data", "sample.srt"));
        Invoke("UpdateSubtitleDraft");
        Click(Field<Button>("SubtitleApplyButton"));
        await Wait(() => Field<bool>("ready"), "baseline subtitle settings did not resume video");
        SendEscape(); await Task.Delay(300);
        await Wait(() => CaptureVideo(input).WhiteBounds().Height > 0, "subtitle was not drawn on video");
        var baselineSubtitles = CaptureVideo(input); baselineSubtitles.Save("subtitle-baseline");
        var baselineBounds = baselineSubtitles.WhiteBounds();
        Click(Field<Button>("SettingsButton")); await Task.Delay(300); Click(Field<Button>("SubtitleTab")); await Task.Delay(200);
        Field<Slider>("SubtitleSize").Value = 150; Field<Slider>("SubtitleMargin").Value = 30;
        long beforeSubtitles = (long)view.Position.TotalMilliseconds;
        Click(Field<Button>("SubtitleApplyButton"));
        await Wait(() => Field<bool>("ready"), "subtitle settings did not resume video");
        await Task.Delay(300);
        Check(view.SubtitleScale == 150 && view.SubtitleMargin == 30, "subtitle settings apply selected size and bottom position");
        var subtitlePlayer = (VlcPlayer)typeof(VideoEngineView).GetField("playback", fields)!.GetValue(view)!;
        await Wait(() => subtitlePlayer.Spu >= 0, "external subtitle track did not activate");
        Check(Math.Abs(view.Position.TotalMilliseconds - beforeSubtitles) < 3000, "subtitle application preserves playback position");
        SaveElement(Field<Grid>("SettingsBackdrop"), "settings-subtitles");
        SendEscape(); await Task.Delay(300);
        await Wait(() => { var bounds = CaptureVideo(input).WhiteBounds(); return bounds.Height > baselineBounds.Height * 1.15 && bounds.Bottom < baselineBounds.Bottom - 15; }, "subtitle size/position did not change in rendered video");
        var changedSubtitles = CaptureVideo(input); changedSubtitles.Save("subtitle-adjusted");
        Check(changedSubtitles.WhiteBounds().Height > baselineBounds.Height * 1.15, "rendered subtitle glyphs grow with selected size");
        Check(changedSubtitles.WhiteBounds().Bottom < baselineBounds.Bottom - 15, "rendered subtitles move upward with selected margin");
        Invoke("ToggleFullscreen"); await Task.Delay(300);
        Move(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight / 2))); await Task.Delay(150);
        Move(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight - 20)));
        await Wait(() => controls.IsOpen, "fullscreen controls not shown for stop");
        await Task.Delay(150);
        var stop = Field<Button>("StopButton"); var play = Field<Button>("PlayButton");
        Check(stop.PointToScreen(new Point()).X > play.PointToScreen(new Point()).X, "stop button is placed to the right of play");
        var stoppedFile = view.Source!.LocalPath;
        Click(stop); await Task.Delay(300);
        Check(window.IsVisible && !Field<bool>("ready") && view.Source == null && view.NaturalVideoWidth == 0, "stop closes the video decoder and keeps the application open");
        Check(Field<Viewbox>("EmptyLogo").IsVisible && !Field<Slider>("Timeline").IsEnabled && Field<TextBlock>("Clock").Text == "0:00 / 0:00", "stop restores the idle logo and clears timeline");
        var fixture = stoppedFile;
        File.Move(fixture, fixture + ".probe"); File.Move(fixture + ".probe", fixture);
        Check(true, "stopped video file can be renamed immediately");
        SendEnter(); await Task.Delay(300);
        Click(Field<Button>("SettingsButton")); await Task.Delay(150); Click(Field<Button>("InfoTab")); await Task.Delay(100);
        Check(Field<TextBlock>("InfoEmpty").IsVisible && Field<ItemsControl>("InfoSections").Items.Count == 0, "stop clears stale media information");
        SendEscape(); await Task.Delay(150);
        await AudioSeekProbe(view);
        Console.WriteLine("Native Windows input, subtitle and decoded audio checks passed.");
    }
    static async Task AudioSeekProbe(VideoEngineView view)
    {
        typeof(MainWindow).GetMethod("OpenFile", fields)!.Invoke(window, new object[] { Path.Combine(AppContext.BaseDirectory, "audio-fixture.mp4") });
        await Wait(() => Field<bool>("ready"), "audio fixture did not open");
        var engine = (VlcPlayer)typeof(VideoEngineView).GetField("playback", fields)!.GetValue(view)!;
        var media = (Media)typeof(VideoEngineView).GetField("media", fields)!.GetValue(view)!;
        long packets = 0;
        engine.Stop();
        engine.SetAudioFormat("S16N", 48000, 1);
        engine.SetAudioCallbacks((_, samples, count, _) =>
        {
            for (int i = 0; i < Math.Min(count, 32); i++)
                if (Math.Abs((int)Marshal.ReadInt16(samples, i * 2)) > 10) { Interlocked.Increment(ref packets); break; }
            Thread.Sleep(Math.Max(1, (int)(count / 48d)));
        }, null, null, null, null);
        engine.Play(media);
        await Wait(() => Interlocked.Read(ref packets) > 2, "decoded audio samples did not arrive");
        var timeline = Field<Slider>("Timeline");
        window.Activate();
        long initialTime = engine.Time;
        long before = Interlocked.Read(ref packets);
        Move(timeline.PointToScreen(new Point(timeline.ActualWidth * .25, timeline.ActualHeight / 2)));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(150);
        Move(timeline.PointToScreen(new Point(timeline.ActualWidth * .5, -14))); await Task.Delay(350);
        Check(timeline.IsMouseCaptured && Field<bool>("seeking"), "timeline captures the mouse outside its thin painted track");
        Check(engine.Time < initialTime + 2500, "timeline drag does not flood native audio with intermediate seeks");
        Check(Interlocked.Read(ref packets) > before, "decoded non-silent audio continues while dragging");
        Check(Field<VideoEngineView>("PreviewPlayer").Source == null, "no preview decoder is opened during scrubbing");
        before = Interlocked.Read(ref packets);
        var elapsed = Stopwatch.StartNew();
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        await Wait(() => engine.Time >= 4500 && Interlocked.Read(ref packets) > before, "audio did not resume after committed seek");
        Check(elapsed.ElapsedMilliseconds < 1500, $"non-silent decoded audio resumes after seek ({elapsed.ElapsedMilliseconds} ms)");
        Check(!timeline.IsMouseCaptured && !Field<bool>("seeking"), "timeline releases capture after outside mouse-up");
        Move(timeline.PointToScreen(new Point(timeline.ActualWidth * .55, timeline.ActualHeight / 2)));
        await Wait(() => Field<VideoEngineView>("PreviewPlayer").Source != null, "preview did not open after normal hover");
        before = Interlocked.Read(ref packets); await Task.Delay(350);
        Check(!engine.Mute && Interlocked.Read(ref packets) > before, "preview does not mute or stall primary decoded audio");
        typeof(MainWindow).GetMethod("StopPlayback", fields)!.Invoke(window, null);
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
            var frame = CaptureVideo(input);
            var actual = (from sy in samples from sx in samples select frame.Pixel(sx, sy)).ToArray();
            return actual.Zip(expected).All(pair => ColorDistance(pair.First, pair.Second) < 55);
        }
        try { await Wait(Match, name + " crop did not render the selected original pixels"); }
        catch { CaptureVideo(input).Save(name + "-crop-failure"); throw; }
        Check(Match(), name + " crop renders selected source colors without distortion");
        var crop = Field<Region>("crop");
        Check(Math.Abs(view.ActualWidth / view.ActualHeight - view.NaturalVideoWidth * crop.Width / (view.NaturalVideoHeight * crop.Height)) < .03,
            name + " viewport preserves selected aspect ratio");
        RightClick(input); keybd_event(0x11, 0, 2, UIntPtr.Zero); await Task.Delay(500);
    }
    sealed record Frame(BitmapSource Image, byte[] Pixels)
    {
        public uint Pixel(double x, double y)
        {
            int i = (Math.Clamp((int)(y * Image.PixelHeight), 0, Image.PixelHeight - 1) * Image.PixelWidth + Math.Clamp((int)(x * Image.PixelWidth), 0, Image.PixelWidth - 1)) * 4;
            return (uint)(Pixels[i+2] | Pixels[i+1] << 8 | Pixels[i] << 16);
        }
        public Rect WhiteBounds()
        {
            int left = Image.PixelWidth, top = Image.PixelHeight, right = -1, bottom = -1;
            for (int y = 0; y < Image.PixelHeight; y++) for (int x = 0; x < Image.PixelWidth; x++)
            {
                int i = (y * Image.PixelWidth + x) * 4;
                if (Pixels[i] > 235 && Pixels[i+1] > 235 && Pixels[i+2] > 235)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            }
            return right < left ? Rect.Empty : new Rect(left, top, right - left + 1, bottom - top + 1);
        }
        public void Save(string name)
        {
            Directory.CreateDirectory("artifacts/ui");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Image));
            using var output = File.Create($"artifacts/ui/{name}.png"); encoder.Save(output);
        }
    }
    static Frame CaptureVideo(FrameworkElement element)
    {
        var a = element.PointToScreen(new Point());
        var b = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        int width = Math.Max(1, (int)(b.X - a.X)), height = Math.Max(1, (int)(b.Y - a.Y));
        var screen = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height); var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, (int)a.X, (int)a.Y, 0x40CC0020)) throw new Exception("Screen capture failed");
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[width * height * 4]; converted.CopyPixels(pixels, width * 4, 0);
            return new Frame(converted, pixels);
        }
        finally { SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }
    static int ColorDistance(uint a, uint b) => Math.Abs((int)(a & 255) - (int)(b & 255)) + Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255)) + Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255));
    static uint ScreenPixel(Point point)
    {
        var dc = GetDC(IntPtr.Zero);
        try { return GetPixel(dc, (int)point.X, (int)point.Y); }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }
    static void Click(FrameworkElement element)
    {
        var point = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        Console.WriteLine($"CLICK {element.Name}: {point}, size {element.ActualWidth}x{element.ActualHeight}, pixel {ScreenPixel(point):X8}");
        Move(point);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
    static void SendEscape() { keybd_event(0x1B, 0, 0, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero); }
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
        var image = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32); 
        if (element.Name is "RootLayout" or "SettingsBackdrop")
        {
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            image.Render(background);
            if (element.Name == "SettingsBackdrop") image.Render(Field<Grid>("RootLayout"));
        }
        image.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create($"artifacts/ui/{name}.png"); png.Save(stream);
    }
    [StructLayout(LayoutKind.Sequential)] readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint mode);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
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
