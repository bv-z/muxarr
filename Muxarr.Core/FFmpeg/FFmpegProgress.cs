namespace Muxarr.Core.FFmpeg;

/// <summary>
/// Turns ffmpeg's -progress output into a percentage. out_time follows the slowest
/// stream, and a subtitle stream only moves when it has a cue: with sparse subtitles,
/// such as a forced track or VobSub, it sat at the last cue while the copy carried on,
/// and a 45 GB MP4 showed 0% for its whole run. Frames written of the first video
/// stream and bytes written keep moving, so the highest of the three is used. Bytes
/// are measured against the source, which a copy never outgrows, so they never run
/// ahead; they only fall short when tracks are dropped.
/// </summary>
public sealed class FFmpegProgress(long durationMs, long totalFrames = 0, long sourceBytes = 0)
{
    public int Percent { get; private set; }

    public int Update(string line)
    {
        var split = line.IndexOf('=');
        if (split <= 0 || !long.TryParse(line.AsSpan(split + 1), out var value))
        {
            return Percent;
        }

        var percent = line[..split] switch
        {
            "out_time_us" when durationMs > 0 => value / 1000 * 100 / durationMs,
            "frame" when totalFrames > 0 => value * 100 / totalFrames,
            "total_size" when sourceBytes > 0 => value * 100 / sourceBytes,
            _ => -1
        };

        if (percent >= 0)
        {
            Percent = Math.Max(Percent, (int)Math.Clamp(percent, 0, 100));
        }

        return Percent;
    }
}
