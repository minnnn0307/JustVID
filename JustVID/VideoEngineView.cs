using System.IO;
using System.Globalization;
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
    public int SubtitleScale { get; private set; } = 100;
    public int SubtitleMargin { get; private set; }
    public string? SubtitleFile { get; private set; }
    long? resumeTime;
    bool resumePaused;
    int? resumeSpu, resumeAudio;
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
            if (resumeTime is long position)
            {
                playback.Time = position;
                if (resumeSpu is int spu) playback.SetSpu(spu);
                if (resumeAudio is int audio && audio >= 0) playback.SetAudioTrack(audio);
                if (resumePaused) playback.SetPause(true);
                resumeTime = null;
            }
            opened = true; opening.Stop(); MediaOpened?.Invoke(this, new RoutedEventArgs());
        };
    }
    public Uri? Source { get => source; set { Close(); source = value; } }
    public int NaturalVideoWidth { get; private set; }
    public int NaturalVideoHeight { get; private set; }
    public Duration NaturalDuration => playback?.Length > 0 ? new Duration(TimeSpan.FromMilliseconds(playback.Length)) : Duration.Automatic;
    public TimeSpan Position { get => TimeSpan.FromMilliseconds(Math.Max(0, playback?.Time ?? 0)); set { if (playback != null) playback.Time = (long)Math.Max(0, value.TotalMilliseconds); } }
    public double Volume { get => volume; set { volume = Math.Clamp(value, 0, 1); if (playback != null && !PreviewMode) playback.Volume = (int)Math.Round(volume * 100); } }
    public bool IsMuted { get => muted; set { muted = value; if (playback != null && !PreviewMode) playback.Mute = muted; } }
    public void Play()
    {
        if (disposed || source == null) return;
        if (playback != null) { if (playback.State == VLCState.Ended) { playback.Stop(); playback.Play(media!); } else playback.SetPause(false); return; }
        try
        {
            int session = generation;
            playback = new VlcPlayer(engine.Value) { EnableKeyInput = false, EnableMouseInput = false, Scale = 0 };
            // No audio API calls for previews: on Windows, session mute/volume can affect the primary player.
            if (!PreviewMode) { playback.Volume = (int)(volume * 100); playback.Mute = muted; }
            playback.EncounteredError += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (!disposed && generation == session) { opening.Stop(); MediaFailed?.Invoke(this, EventArgs.Empty); } }));
            playback.EndReached += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (!disposed && generation == session) MediaEnded?.Invoke(this, new RoutedEventArgs()); }));
            media = new Media(engine.Value, source);
            media.AddOption(":avcodec-hw=any");
            if (PreviewMode) { media.AddOption(":no-audio"); media.AddOption(":input-fast-seek"); }
            else
            {
                media.AddOption(":freetype-fontsize=0");
                media.AddOption(":freetype-rel-fontsize=" + Math.Clamp((int)Math.Round(1600d / SubtitleScale), 4, 40));
                media.AddOption(":sub-margin=" + SubtitleMargin);
                if (SubtitleFile != null) media.AddSlave(MediaSlaveType.Subtitle, 4, new Uri(SubtitleFile).AbsoluteUri);
                if (resumeTime is long start) media.AddOption(":start-time=" + (start / 1000d).ToString(CultureInfo.InvariantCulture));
                if (resumeTime.HasValue && resumePaused) media.AddOption(":start-paused");
            }
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
    public bool ConfigureSubtitles(int scale, int margin, string? file)
    {
        scale = Math.Clamp(scale, 50, 200); margin = Math.Clamp(margin, 0, 300);
        if (SubtitleScale == scale && SubtitleMargin == margin && SubtitleFile == file) return false;
        bool fileChanged = SubtitleFile != file;
        SubtitleScale = scale; SubtitleMargin = margin; SubtitleFile = file;
        if (playback == null || source == null || PreviewMode) return false;
        long time = Math.Max(0, playback.Time);
        bool paused = !playback.IsPlaying;
        int? spu = fileChanged ? null : playback.Spu;
        int audio = playback.AudioTrack;
        Close();
        resumeTime = time; resumePaused = paused; resumeSpu = spu; resumeAudio = audio;
        Play();
        return true;
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
        resumeTime = null; resumeSpu = resumeAudio = null;
        generation++; opening.Stop(); opened = false; NaturalVideoWidth = NaturalVideoHeight = 0;
        MediaPlayer = null;
        playback?.Stop(); playback?.Dispose(); playback = null;
        media?.Dispose(); media = null;
    }
    public new void Dispose() { if (disposed) return; disposed = true; Close(); base.Dispose(); }
}
