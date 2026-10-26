using System.Globalization;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace JustVID;
public partial class MainWindow : Window
{
    readonly DispatcherTimer fullscreenIdle = new() { Interval = TimeSpan.FromMilliseconds(100) };
    Point lastPointer;
    long controlsActivity;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer previewDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly DispatcherTimer previewRelease = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JustVID", "settings.json");
    Region crop = new(0, 0, 1, 1);
    Rect viewport;
    Point? origin;
    bool playing, updating, seeking, ready, previewReady, light, fullscreen;
    double previewTarget;
    Uri? source;
    readonly WindowFrame frame;
    public string Publisher => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? "조민혁" : "Minhyeok Cho";
    public MainWindow()
    {
        InitializeComponent();
        frame = new WindowFrame(this);
        fullscreenIdle.Tick += (_, _) => UpdateFullscreenControls();
        Player.Volume = Volume.Value; UpdateVolumeIcon();
        clock.Tick += (_, _) => { if (!ready || seeking) return; updating = true; Timeline.Value = Player.Position.TotalSeconds; updating = false; UpdateClock(); };
        previewDelay.Tick += (_, _) => { previewDelay.Stop(); ShowPreview(); };
        previewRelease.Tick += (_, _) => { previewRelease.Stop(); ReleasePreview(); };
        Loaded += (_, _) => {
            try { if (File.Exists(settingsPath)) { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)); if (s != null) { Volume.Value = Math.Clamp(s.Volume, 0, 1); light = s.Light; ApplyTheme(); } } } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
            var args = Environment.GetCommandLineArgs(); if (args.Length > 1 && File.Exists(args[1])) OpenFile(args[1]);
        };
        Closed += (_, _) => {
            fullscreenIdle.Stop(); FullscreenControls.IsOpen = false; clock.Stop(); previewDelay.Stop(); previewRelease.Stop(); Player.Dispose(); PreviewPlayer.Dispose();
            try { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath, JsonSerializer.Serialize(new Settings(light, Volume.Value))); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };
        SizeChanged += (_, _) => { Volume.Visibility = ActualWidth < 430 ? Visibility.Collapsed : Visibility.Visible; Clock.Visibility = ActualWidth < 330 ? Visibility.Collapsed : Visibility.Visible; FileName.Visibility = ActualWidth < 440 ? Visibility.Collapsed : Visibility.Visible; };
    }
    record Settings(bool Light, double Volume);
    void OpenClick(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "Video|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm|All files|*.*" }; if (dialog.ShowDialog(this) == true) OpenFile(dialog.FileName); }
    void FileDrop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) OpenFile(files[0]); }
    void OpenFile(string path)
    {
        clock.Stop(); ReleasePreview(); CancelSelection(); ready = false; playing = false; Timeline.IsEnabled = false;
        Player.Close(); crop = new(0, 0, 1, 1); FileName.Text = Path.GetFileName(path); EmptyLogo.Visibility = Visibility.Collapsed;
        source = new Uri(Path.GetFullPath(path)); Player.Source = source; Player.Visibility = Visibility.Visible; Player.Play(); playing = true; PlayButton.Content = "Ⅱ";
    }
    void MediaOpened(object sender, RoutedEventArgs e)
    {
        ready = Player.NaturalDuration.HasTimeSpan && Player.NaturalDuration.TimeSpan.TotalSeconds > 0;
        updating = true; Timeline.Maximum = ready ? Player.NaturalDuration.TimeSpan.TotalSeconds : 1; Timeline.Value = 0; updating = false;
        Timeline.IsEnabled = ready; ArrangeVideo(); clock.Start(); UpdateClock();
    }
    void MediaEnded(object sender, RoutedEventArgs e) { Player.Pause(); playing = false; PlayButton.Content = "▶"; }
    void MediaFailed(object sender, EventArgs e)
    {
        clock.Stop(); ready = playing = false; Timeline.IsEnabled = false; Player.Close(); Player.Visibility = Visibility.Hidden; ReleasePreview(); EmptyLogo.Visibility = Visibility.Visible; PlayButton.Content = "▶";
        MessageBox.Show(this, "영상을 열 수 없습니다. 손상되었거나 지원하지 않는 파일일 수 있습니다.\nThe file may be damaged or unsupported.", "JustVID", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    void ToggleClick(object sender, RoutedEventArgs e) => Toggle();
    void Toggle() { if (!ready) return; if (playing) Player.Pause(); else { if (Player.Position.TotalSeconds >= Timeline.Maximum - .05) Player.Position = TimeSpan.Zero; Player.Play(); } playing = !playing; PlayButton.Content = playing ? "Ⅱ" : "▶"; }
    void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Player != null) { Player.Volume = e.NewValue; if (e.NewValue > 0) Player.IsMuted = false; UpdateVolumeIcon(); } }
    void MuteClick(object sender, RoutedEventArgs e) { Player.IsMuted = !Player.IsMuted; UpdateVolumeIcon(); }
    void UpdateVolumeIcon()
    {
        if (VolumeOff == null || VolumeLow == null || VolumeHigh == null) return;
        bool silent = Player.IsMuted || Player.Volume <= 0;
        VolumeOff.Visibility = silent ? Visibility.Visible : Visibility.Collapsed;
        VolumeLow.Visibility = !silent ? Visibility.Visible : Visibility.Collapsed;
        VolumeHigh.Visibility = !silent && Player.Volume > .5 ? Visibility.Visible : Visibility.Collapsed;
        MuteButton.ToolTip = silent ? "음소거 해제 / Unmute (M)" : $"볼륨 / Volume {Player.Volume:P0} (M)";
    }
    static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    void UpdateClock() => Clock.Text = $"{Time(Player.Position.TotalSeconds)} / {Time(Timeline.Maximum)}";
    void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ready && !updating) { Player.Position = TimeSpan.FromSeconds(e.NewValue); UpdateClock(); } }
    void SeekStart(object sender, MouseButtonEventArgs e) { seeking = true; }
    void SeekEnd(object sender, MouseButtonEventArgs e) { seeking = false; }
    void PreviewMove(object sender, MouseEventArgs e)
    {
        if (!ready) return;
        var p = e.GetPosition(Timeline); previewTarget = Math.Clamp((p.X - 10) / Math.Max(1, Timeline.ActualWidth - 20), 0, 1) * Timeline.Maximum;
        PreviewTime.Text = Time(previewTarget); PreviewPopup.HorizontalOffset = Math.Clamp(p.X - 88, 0, Math.Max(0, Timeline.ActualWidth - 178)); PreviewPopup.VerticalOffset = -134;
        previewRelease.Stop(); if (!previewDelay.IsEnabled) previewDelay.Start();
    }
    void ShowPreview()
    {
        if (!Timeline.IsMouseOver || source == null) return;
        PreviewPopup.IsOpen = true;
        if (PreviewPlayer.Source == null) { PreviewStatus.Text = "…"; PreviewStatus.Visibility = Visibility.Visible; PreviewPlayer.Source = source; PreviewPlayer.Play(); }
        else if (previewReady) PreviewPlayer.Position = TimeSpan.FromSeconds(previewTarget);
    }
    void PreviewOpened(object sender, RoutedEventArgs e) { previewReady = true; PreviewPlayer.Pause(); PreviewPlayer.Position = TimeSpan.FromSeconds(previewTarget); PreviewStatus.Visibility = Visibility.Collapsed; }
    void PreviewFailed(object sender, EventArgs e) { previewReady = false; PreviewStatus.Text = "미리보기 없음 / Unavailable"; PreviewStatus.Visibility = Visibility.Visible; }
    void PreviewLeave(object sender, MouseEventArgs e) { seeking = false; previewDelay.Stop(); PreviewPopup.IsOpen = false; previewRelease.Start(); }
    void ReleasePreview() { previewDelay.Stop(); previewRelease.Stop(); PreviewPopup.IsOpen = false; PreviewPlayer.Close(); PreviewPlayer.Source = null; previewReady = false; }
    void LayoutVideo(object sender, SizeChangedEventArgs e) { CancelSelection(); ArrangeVideo(); }
    void ArrangeVideo()
    {
        if (Player.NaturalVideoWidth <= 0 || Player.NaturalVideoHeight <= 0 || VideoArea.ActualHeight <= 0) return;
        double aspect = Player.NaturalVideoWidth * crop.Width / (Player.NaturalVideoHeight * crop.Height);
        double w = VideoArea.ActualWidth, h = w / aspect;
        if (h > VideoArea.ActualHeight) { h = VideoArea.ActualHeight; w = h * aspect; }
        viewport = new Rect(0, 0, w, h);
        Player.Width = w; Player.Height = h;
        Player.Margin = new Thickness((VideoArea.ActualWidth - w) / 2, (VideoArea.ActualHeight - h) / 2, 0, 0);
        Player.SetCrop(crop);

    }
    Point ClampPoint(Point p) => new(Math.Clamp(p.X, viewport.Left, viewport.Right), Math.Clamp(p.Y, viewport.Top, viewport.Bottom));
    void VideoDown(object sender, MouseButtonEventArgs e)
    {
        if (!ready || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || !viewport.Contains(e.GetPosition(InputArea))) return;
        origin = ClampPoint(e.GetPosition(InputArea)); InputArea.Focus(); InputArea.CaptureMouse(); Selection.Visibility = Visibility.Visible; DrawSelection(origin.Value); e.Handled = true;
    }
    void DrawSelection(Point p) { if (origin is not Point start) return; Canvas.SetLeft(Selection, Math.Min(start.X, p.X)); Canvas.SetTop(Selection, Math.Min(start.Y, p.Y)); Selection.Width = Math.Max(1, Math.Abs(p.X - start.X)); Selection.Height = Math.Max(1, Math.Abs(p.Y - start.Y)); }
    void VideoMove(object sender, MouseEventArgs e) { if (origin != null) DrawSelection(ClampPoint(e.GetPosition(InputArea))); }
    void VideoUp(object sender, MouseButtonEventArgs e)
    {
        if (origin is not Point start) return;
        var end = ClampPoint(e.GetPosition(InputArea));
        if ((end - start).Length > 0 && viewport.Width > 0 && viewport.Height > 0) {
            // A one-pixel selection is valid. Preserve the selected image's source aspect ratio.
            var next = ZoomMath.Select(crop, start.X - viewport.X, start.Y - viewport.Y, end.X - viewport.X, end.Y - viewport.Y, viewport.Width, viewport.Height);
            var pixels = ZoomMath.Pixels(next, Player.NaturalVideoWidth, Player.NaturalVideoHeight);
            crop = new Region((double)pixels.X / Player.NaturalVideoWidth, (double)pixels.Y / Player.NaturalVideoHeight,
                (double)pixels.Width / Player.NaturalVideoWidth, (double)pixels.Height / Player.NaturalVideoHeight);
        }
        CancelSelection(); ArrangeVideo(); e.Handled = true;
    }
    void SelectionLost(object sender, MouseEventArgs e) { origin = null; Selection.Visibility = Visibility.Collapsed; }
    void CancelSelection() { origin = null; Selection.Visibility = Visibility.Collapsed; if (InputArea.IsMouseCaptured) InputArea.ReleaseMouseCapture(); }
    void VideoRight(object sender, MouseButtonEventArgs e) { if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return; CancelSelection(); crop = new(0, 0, 1, 1); ArrangeVideo(); e.Handled = true; }
    void ThemeClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var item in new[] { ("다크 / Dark", false), ("라이트 / Light", true) }) { var m = new MenuItem { Header = item.Item1, IsCheckable = true, IsChecked = light == item.Item2 }; m.Click += (_, _) => { light = item.Item2; ApplyTheme(); }; menu.Items.Add(m); }
        menu.Items.Add(new Separator()); menu.Items.Add(new MenuItem { Header = $"JustVID · 1.1.0 · {Publisher}", IsEnabled = false }); menu.IsOpen = true;
    }
    void ApplyTheme()
    {
        var colors = light ? new[] { "#F5F6F7", "#202428", "#626A72", "#D4D9DE", "#E4E8EC" } : new[] { "#17191C", "#F3F4F5", "#9CA3AB", "#3A3E44", "#30353A" };
        Resources["VideoBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#E7EAED" : "#101214"));
        var keys = new[] { "Surface", "Ink", "Muted", "TrackColor", "Hover" }; for (int i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
    void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize(object sender, RoutedEventArgs e) { if (fullscreen) ExitFullscreen(); else WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
    void CloseWindow(object sender, RoutedEventArgs e) => Close();
    void FullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    void ToggleFullscreen()
    {
        if (fullscreen) { ExitFullscreen(); return; }
        CancelSelection(); ReleasePreview(); fullscreen = true;
        TitleBar.Visibility = Visibility.Collapsed; TitleRow.Height = new GridLength(0);
        RootLayout.Children.Remove(ControlBar);
        FullscreenControlHost.Child = ControlBar;
        frame.EnterFullscreen();
        PositionFullscreenControls();
        if (GetCursorPos(out var cursor)) lastPointer = new Point(cursor.X, cursor.Y);
        fullscreenIdle.Start();
    }
    void ExitFullscreen()
    {
        if (!fullscreen) return;
        CancelSelection(); fullscreen = false;
        fullscreenIdle.Stop(); FullscreenControls.IsOpen = false; ReleasePreview();
        FullscreenControlHost.Child = null; RootLayout.Children.Add(ControlBar);
        TitleBar.Visibility = Visibility.Visible; TitleRow.Height = new GridLength(40);
        frame.ExitFullscreen();
    }
    void PositionFullscreenControls()
    {
        FullscreenControlHost.Width = Math.Max(1, ActualWidth);
        FullscreenControls.HorizontalOffset = 0;
        FullscreenControls.VerticalOffset = Math.Max(0, ActualHeight - 75);
    }
    void UpdateFullscreenControls()
    {
        if (!fullscreen || !GetCursorPos(out var cursor)) return;
        var pointer = new Point(cursor.X, cursor.Y);
        var local = PointFromScreen(pointer);
        bool moved = pointer != lastPointer;
        lastPointer = pointer;
        bool inWindow = local.X >= 0 && local.X < ActualWidth && local.Y >= 0 && local.Y < ActualHeight;
        bool appActive = IsActive || Window.GetWindow(InputArea)?.IsActive == true || FullscreenControlHost.IsKeyboardFocusWithin;
        if (!appActive) { FullscreenControls.IsOpen = false; ReleasePreview(); return; }
        long now = Stopwatch.GetTimestamp();
        if (moved && inWindow && local.Y >= ActualHeight - 84)
        {
            controlsActivity = now; PositionFullscreenControls(); FullscreenControls.IsOpen = true;
        }
        if (!FullscreenControls.IsOpen) return;
        if (ControlBar.IsMouseCaptureWithin || seeking || origin != null || (inWindow && Mouse.LeftButton == MouseButtonState.Pressed)) controlsActivity = now;
        if ((now - controlsActivity) / (double)Stopwatch.Frequency >= 2.5)
        {
            FullscreenControls.IsOpen = false; ReleasePreview();
            // A closed popup must not keep keyboard focus in its old child tree.
            if (ready) InputArea.Focus(); else Focus();
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out CursorPoint point);
    void KeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) OpenClick(this, e);
        else if (e.Key == Key.Space) Toggle();
        else if (e.Key == Key.F) ToggleFullscreen();
        else if (e.Key == Key.Escape)
        {
            CancelSelection();
            if (fullscreen) ExitFullscreen();
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        }
        else if (e.Key == Key.M) MuteClick(this, e);
        else if (ready && (e.Key == Key.Left || e.Key == Key.Right)) Player.Position = TimeSpan.FromSeconds(Math.Clamp(Player.Position.TotalSeconds + (e.Key == Key.Right ? 5 : -5), 0, Timeline.Maximum));
        else return;
        e.Handled = true;
    }
}
