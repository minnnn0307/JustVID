using System.Globalization;
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
    WindowState previousState;
    public string Publisher => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? "조민혁" : "Minhyeok Cho";
    public MainWindow()
    {
        InitializeComponent();
        Player.Volume = Volume.Value;
        clock.Tick += (_, _) => { if (!ready || seeking) return; updating = true; Timeline.Value = Player.Position.TotalSeconds; updating = false; UpdateClock(); };
        previewDelay.Tick += (_, _) => { previewDelay.Stop(); ShowPreview(); };
        previewRelease.Tick += (_, _) => { previewRelease.Stop(); ReleasePreview(); };
        Loaded += (_, _) => {
            try { if (File.Exists(settingsPath)) { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)); if (s != null) { Volume.Value = Math.Clamp(s.Volume, 0, 1); light = s.Light; ApplyTheme(); } } } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
            var args = Environment.GetCommandLineArgs(); if (args.Length > 1 && File.Exists(args[1])) OpenFile(args[1]);
        };
        Closed += (_, _) => {
            clock.Stop(); previewDelay.Stop(); previewRelease.Stop(); Player.Close(); PreviewPlayer.Close();
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
        Player.Close(); crop = new(0, 0, 1, 1); FileName.Text = Path.GetFileName(path); OpenHint.Visibility = Visibility.Collapsed;
        source = new Uri(Path.GetFullPath(path)); Player.Source = source; Player.Play(); playing = true; PlayButton.Content = "Ⅱ";
    }
    void MediaOpened(object sender, RoutedEventArgs e)
    {
        ready = Player.NaturalDuration.HasTimeSpan && Player.NaturalDuration.TimeSpan.TotalSeconds > 0;
        updating = true; Timeline.Maximum = ready ? Player.NaturalDuration.TimeSpan.TotalSeconds : 1; Timeline.Value = 0; updating = false;
        Timeline.IsEnabled = ready; ArrangeVideo(); clock.Start(); UpdateClock();
    }
    void MediaEnded(object sender, RoutedEventArgs e) { Player.Pause(); playing = false; PlayButton.Content = "▶"; }
    void MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        clock.Stop(); ready = playing = false; Timeline.IsEnabled = false; Player.Close(); ReleasePreview(); OpenHint.Visibility = Visibility.Visible; PlayButton.Content = "▶";
        MessageBox.Show(this, "영상을 열 수 없습니다. 파일 또는 Windows 코덱 지원을 확인하세요.\nUnable to open video. Check the file and Windows codec support.", "JustVID", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    void ToggleClick(object sender, RoutedEventArgs e) => Toggle();
    void Toggle() { if (!ready) return; if (playing) Player.Pause(); else { if (Player.Position.TotalSeconds >= Timeline.Maximum - .05) Player.Position = TimeSpan.Zero; Player.Play(); } playing = !playing; PlayButton.Content = playing ? "Ⅱ" : "▶"; }
    void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Player != null) Player.Volume = e.NewValue; }
    void MuteClick(object sender, RoutedEventArgs e) { Player.IsMuted = !Player.IsMuted; MuteButton.Opacity = Player.IsMuted ? .4 : 1; }
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
    void PreviewFailed(object sender, ExceptionRoutedEventArgs e) { previewReady = false; PreviewStatus.Text = "미리보기 없음 / Unavailable"; PreviewStatus.Visibility = Visibility.Visible; }
    void PreviewLeave(object sender, MouseEventArgs e) { seeking = false; previewDelay.Stop(); PreviewPopup.IsOpen = false; previewRelease.Start(); }
    void ReleasePreview() { previewDelay.Stop(); previewRelease.Stop(); PreviewPopup.IsOpen = false; PreviewPlayer.Close(); PreviewPlayer.Source = null; previewReady = false; }
    void LayoutVideo(object sender, SizeChangedEventArgs e) { CancelSelection(); ArrangeVideo(); }
    void ArrangeVideo()
    {
        if (Player.NaturalVideoWidth <= 0 || VideoArea.ActualHeight <= 0) return;
        double aspect = Player.NaturalVideoWidth * crop.Width / (Player.NaturalVideoHeight * crop.Height);
        double w = VideoArea.ActualWidth, h = w / aspect;
        if (h > VideoArea.ActualHeight) { h = VideoArea.ActualHeight; w = h * aspect; }
        viewport = new Rect((VideoArea.ActualWidth - w) / 2, (VideoArea.ActualHeight - h) / 2, w, h);
        Player.Width = w / crop.Width; Player.Height = h / crop.Height;
        Player.Margin = new Thickness(viewport.X - crop.X * Player.Width, viewport.Y - crop.Y * Player.Height, 0, 0);
        Player.Clip = new RectangleGeometry(new Rect(crop.X * Player.Width, crop.Y * Player.Height, w, h));
    }
    Point ClampPoint(Point p) => new(Math.Clamp(p.X, viewport.Left, viewport.Right), Math.Clamp(p.Y, viewport.Top, viewport.Bottom));
    void VideoDown(object sender, MouseButtonEventArgs e)
    {
        if (!ready || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || !viewport.Contains(e.GetPosition(VideoArea))) return;
        origin = ClampPoint(e.GetPosition(VideoArea)); VideoArea.CaptureMouse(); Selection.Visibility = Visibility.Visible; DrawSelection(origin.Value); e.Handled = true;
    }
    void DrawSelection(Point p) { if (origin is not Point start) return; Canvas.SetLeft(Selection, Math.Min(start.X, p.X)); Canvas.SetTop(Selection, Math.Min(start.Y, p.Y)); Selection.Width = Math.Max(1, Math.Abs(p.X - start.X)); Selection.Height = Math.Max(1, Math.Abs(p.Y - start.Y)); }
    void VideoMove(object sender, MouseEventArgs e) { if (origin != null) DrawSelection(ClampPoint(e.GetPosition(VideoArea))); }
    void VideoUp(object sender, MouseButtonEventArgs e)
    {
        if (origin is not Point start) return;
        var end = ClampPoint(e.GetPosition(VideoArea));
        if ((end - start).Length > 0 && viewport.Width > 0 && viewport.Height > 0) {
            // A one-pixel selection is valid. Preserve the selected image's source aspect ratio.
            var next = ZoomMath.Select(crop, start.X - viewport.X, start.Y - viewport.Y, end.X - viewport.X, end.Y - viewport.Y, viewport.Width, viewport.Height);
            if (next.Width > 1e-9 && next.Height > 1e-9) crop = next;
        }
        CancelSelection(); ArrangeVideo(); e.Handled = true;
    }
    void SelectionLost(object sender, MouseEventArgs e) { origin = null; Selection.Visibility = Visibility.Collapsed; }
    void CancelSelection() { origin = null; Selection.Visibility = Visibility.Collapsed; if (VideoArea.IsMouseCaptured) VideoArea.ReleaseMouseCapture(); }
    void VideoRight(object sender, MouseButtonEventArgs e) { if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return; CancelSelection(); crop = new(0, 0, 1, 1); ArrangeVideo(); e.Handled = true; }
    void ThemeClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var item in new[] { ("다크 / Dark", false), ("라이트 / Light", true) }) { var m = new MenuItem { Header = item.Item1, IsCheckable = true, IsChecked = light == item.Item2 }; m.Click += (_, _) => { light = item.Item2; ApplyTheme(); }; menu.Items.Add(m); }
        menu.Items.Add(new Separator()); menu.Items.Add(new MenuItem { Header = $"JustVID · 1.0.0 · {Publisher}", IsEnabled = false }); menu.IsOpen = true;
    }
    void ApplyTheme()
    {
        var colors = light ? new[] { "#F5F6F7", "#202428", "#626A72", "#D4D9DE", "#E4E8EC" } : new[] { "#17191C", "#F3F4F5", "#9CA3AB", "#3A3E44", "#30353A" };
        var keys = new[] { "Surface", "Ink", "Muted", "TrackColor", "Hover" }; for (int i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
    void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize(object sender, RoutedEventArgs e) { if (fullscreen) ExitFullscreen(); else WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
    void CloseWindow(object sender, RoutedEventArgs e) => Close();
    void FullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    void ToggleFullscreen() { if (fullscreen) ExitFullscreen(); else { previousState = WindowState; fullscreen = true; WindowState = WindowState.Normal; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; } }
    void ExitFullscreen() { fullscreen = false; WindowState = WindowState.Normal; ResizeMode = ResizeMode.CanResize; WindowState = previousState; }
    void KeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) OpenClick(this, e);
        else if (e.Key == Key.Space) Toggle();
        else if (e.Key == Key.F) ToggleFullscreen();
        else if (e.Key == Key.Escape) { CancelSelection(); if (fullscreen) ExitFullscreen(); }
        else if (e.Key == Key.M) MuteClick(this, e);
        else if (ready && (e.Key == Key.Left || e.Key == Key.Right)) Player.Position = TimeSpan.FromSeconds(Math.Clamp(Player.Position.TotalSeconds + (e.Key == Key.Right ? 5 : -5), 0, Timeline.Maximum));
        else return;
        e.Handled = true;
    }
}
