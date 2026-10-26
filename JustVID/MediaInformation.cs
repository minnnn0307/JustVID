using System.Globalization;
using System.IO;
using System.Text;
using LibVLCSharp.Shared;

namespace JustVID;
public sealed record InformationRow(string Label, string Value);
public sealed record InformationSection(string Title, IReadOnlyList<InformationRow> Rows);

public static class MediaInformation
{
    public static IReadOnlyList<InformationSection> Read(Media media, Uri source, long duration)
    {
        var sections = new List<InformationSection>();
        var file = new FileInfo(source.LocalPath);
        var fileRows = new List<InformationRow> {
            new("이름 / Name", file.Name), new("위치 / Path", file.FullName),
            new("확장자 / Extension", file.Extension.ToUpperInvariant()),
            new("재생 시간 / Duration", duration > 0 ? TimeSpan.FromMilliseconds(duration).ToString(@"hh\:mm\:ss\.fff") : "확인 불가 / Unavailable")
        };
        if (file.Exists)
        {
            fileRows.Add(new("크기 / Size", $"{file.Length / 1048576d:N2} MiB ({file.Length:N0} bytes)"));
            fileRows.Add(new("수정 시각 / Modified", file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")));
            if (duration > 0) fileRows.Add(new("평균 전송률 / Avg. bitrate", $"{file.Length * 8d / duration:N0} kb/s (파일 크기 기준 / from file size)"));
        }
        sections.Add(new("파일 / File", fileRows));
        int videos = 0, audios = 0, subtitles = 0;
        foreach (var track in media.Tracks)
        {
            var rows = new List<InformationRow> { new("코덱 / Codec", Codec(track.Codec)) };
            if (!string.IsNullOrWhiteSpace(track.Language)) rows.Add(new("언어 / Language", track.Language!));
            if (!string.IsNullOrWhiteSpace(track.Description)) rows.Add(new("설명 / Description", track.Description!));
            if (track.Bitrate > 0) rows.Add(new("비트레이트 / Bitrate", $"{track.Bitrate / 1000d:N0} kb/s"));
            string title;
            if (track.TrackType == TrackType.Video)
            {
                title = $"영상 / Video {++videos}";
                var v = track.Data.Video;
                rows.Add(new("해상도 / Resolution", $"{v.Width} × {v.Height}"));
                if (v.FrameRateDen > 0) rows.Add(new("프레임률 / Frame rate", $"{v.FrameRateNum / (double)v.FrameRateDen:0.###} fps ({v.FrameRateNum}/{v.FrameRateDen})"));
                if (v.SarNum > 0 && v.SarDen > 0)
                {
                    rows.Add(new("픽셀 비율 / Pixel ratio", $"{v.SarNum}:{v.SarDen}"));
                    ulong width = (ulong)v.Width * v.SarNum, height = (ulong)v.Height * v.SarDen;
                    ulong gcd = Gcd(width, height);
                    if (gcd > 0) rows.Add(new("화면 비율 / Aspect ratio", $"{width/gcd}:{height/gcd}"));
                }
                rows.Add(new("방향 / Orientation", v.Orientation.ToString()));
                if (track.Profile >= 0) rows.Add(new("프로파일 / Profile", track.Profile.ToString(CultureInfo.InvariantCulture)));
                if (track.Level > 0) rows.Add(new("레벨 / Level", track.Level.ToString(CultureInfo.InvariantCulture)));
            }
            else if (track.TrackType == TrackType.Audio)
            {
                title = $"음성 / Audio {++audios}";
                rows.Add(new("채널 / Channels", track.Data.Audio.Channels.ToString(CultureInfo.InvariantCulture)));
                rows.Add(new("샘플레이트 / Sample rate", $"{track.Data.Audio.Rate:N0} Hz"));
            }
            else if (track.TrackType == TrackType.Text)
            {
                title = $"자막 / Subtitles {++subtitles}";
                if (!string.IsNullOrWhiteSpace(track.Data.Subtitle.Encoding)) rows.Add(new("문자 인코딩 / Encoding", track.Data.Subtitle.Encoding!));
            }
            else continue;
            sections.Add(new(title, rows));
        }
        var summary = new List<InformationRow> { new("트랙 / Tracks", $"영상 {videos} · 음성 {audios} · 자막 {subtitles}") };
        sections.Insert(1, new("스트림 / Streams", summary));
        var tags = new List<InformationRow>();
        foreach (MetadataType tag in Enum.GetValues<MetadataType>())
        {
            if (tag == MetadataType.ArtworkURL) continue;
            var value = media.Meta(tag);
            if (!string.IsNullOrWhiteSpace(value)) tags.Add(new(tag.ToString(), value!));
        }
        if (tags.Count > 0) sections.Add(new("메타데이터 / Metadata", tags));
        return sections;
    }
    static ulong Gcd(ulong a, ulong b) { while (b != 0) (a, b) = (b, a % b); return a; }
    static string Codec(uint codec)
    {
        var code = Encoding.ASCII.GetString(BitConverter.GetBytes(codec)).Trim('\0', ' ');
        var name = code.ToLowerInvariant() switch {
            "h264" => "H.264 / AVC", "hevc" => "H.265 / HEVC", "av01" => "AV1", "vp80" => "VP8", "vp90" => "VP9",
            "mp4a" => "AAC", "opus" => "Opus", "vorb" => "Vorbis", "flac" => "FLAC", "ac-3" => "AC-3", "eac3" => "E-AC-3", _ => code
        };
        return string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) ? $"0x{codec:X8}" : $"{name} ({code})";
    }
}
