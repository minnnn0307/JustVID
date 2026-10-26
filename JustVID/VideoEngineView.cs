using System.IO;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace JustVID;

// VideoView content is a native-safe WPF overlay: selections remain above the video.
public sealed class VideoEngineView : VideoView
{
    static readonly Lazy<LibVLC> engine = new(() =>
    {
        Core.Initialize(Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64"));
        return new LibVLC("--no-video-title-show", "--no-osd", "--no-stats");
    });
    readonly DispatcherTimer opening = new() { Interval = TimeSpan.FromMilliseconds(100) };
    VlcPlayer? playback;
    Media? media;
    Uri? source;
    bool opened, disposed, muted;
    double volume = .65;
    int generation;
    public bool PreviewMode { get; set; }
    public event RoutedEventHandler? MediaOpened;
    public event RoutedEventHandler? MediaEnded;
    public event EventHandler? MediaFailed;
    public VideoEngineView()
    {
        opening.Tick += (_, _) =>
        {
            if (playback == null || opened || playback.Length <= 0) return;
            uint w = 0, h = 0;
            if (!playback.Size(0, ref w, ref h) || w == 0 || h == 0) return;
            NaturalVideoWidth = (int)w; NaturalVideoHeight = (int)h;
            opened = true; opening.Stop(); MediaOpened?.Invoke(this, new RoutedEventArgs());
        };
    }
    public Uri? Source { get => source; set { Close(); source = value; } }
    public int NaturalVideoWidth { get; private set; }
    public int NaturalVideoHeight { get; private set; }
    public Duration NaturalDuration => playback?.Length > 0 ? new Duration(TimeSpan.FromMilliseconds(playback.Length)) : Duration.Automatic;
    public TimeSpan Position { get => TimeSpan.FromMilliseconds(Math.Max(0, playback?.Time ?? 0)); set { if (playback != null) playback.Time = (long)Math.Max(0, value.TotalMilliseconds); } }
    public double Volume { get => volume; set { volume = Math.Clamp(value, 0, 1); if (playback != null) playback.Volume = (int)Math.Round(volume * 100); } }
    public bool IsMuted { get => muted; set { muted = value; if (playback != null) playback.Mute = muted; } }
    public void Play()
    {
        if (disposed || source == null) return;
        if (playback != null) { if (playback.State == VLCState.Ended) { playback.Stop(); playback.Play(media!); } else playback.SetPause(false); return; }
        try
        {
            int session = generation;
            playback = new VlcPlayer(engine.Value) { EnableKeyInput = false, EnableMouseInput = false, Volume = (int)(volume * 100), Mute = muted, Scale = 0 };
            playback.EncounteredError += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (!disposed && generation == session) { opening.Stop(); MediaFailed?.Invoke(this, EventArgs.Empty); } }));
            playback.EndReached += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (!disposed && generation == session) MediaEnded?.Invoke(this, new RoutedEventArgs()); }));
            media = new Media(engine.Value, source);
            media.AddOption(":avcodec-hw=any");
            if (PreviewMode) { media.AddOption(":no-audio"); media.AddOption(":input-fast-seek"); }
            MediaPlayer = playback;
            opening.Start();
            if (!playback.Play(media)) { opening.Stop(); MediaFailed?.Invoke(this, EventArgs.Empty); }
        }
        catch (Exception ex) when (ex is VLCException or DllNotFoundException or BadImageFormatException)
        { opening.Stop(); MediaFailed?.Invoke(this, EventArgs.Empty); }
    }
    public void Pause() => playback?.SetPause(true);
    public void SetCrop(Region region)
    {
        if (playback == null || NaturalVideoWidth <= 0 || NaturalVideoHeight <= 0) return;
        playback.CropGeometry = ZoomMath.CropGeometry(region, NaturalVideoWidth, NaturalVideoHeight);
    }
    public IReadOnlyList<InformationSection> GetInformation()
    {
        if (media == null || source == null) return Array.Empty<InformationSection>();
        try { return MediaInformation.Read(media, source, playback?.Length ?? 0); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or VLCException)
        { return new[] { new InformationSection("정보 / Information", new[] { new InformationRow("상태 / Status", "일부 정보를 읽을 수 없습니다. / Some information is unavailable.") }) }; }
    }
    public void Close()
    {
        generation++; opening.Stop(); opened = false; NaturalVideoWidth = NaturalVideoHeight = 0;
        MediaPlayer = null;
        playback?.Stop(); playback?.Dispose(); playback = null;
        media?.Dispose(); media = null;
    }
    public new void Dispose() { if (disposed) return; disposed = true; Close(); base.Dispose(); }
}
