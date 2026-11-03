using Muxarr.Core.FFmpeg;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

/// <summary>
/// ffmpeg's out_time follows the slowest stream. A 45 GB MP4 with VobSub
/// subtitles reported time=N/A for its whole 17-minute run and sat at 0%.
/// </summary>
[TestClass]
public class FFmpegProgressTests
{
    [TestMethod]
    public void TimeNotAvailable_FollowsFramesWritten()
    {
        var progress = new FFmpegProgress(durationMs: 6_661_990, totalFrames: 159_728, sourceBytes: 48_554_000_000);

        progress.Update("frame=79864");
        progress.Update("out_time_us=N/A");

        Assert.AreEqual(50, progress.Percent);
    }

    [TestMethod]
    public void TimeStuckAtTheLastSubtitleCue_FollowsBytesWritten()
    {
        // Recorded from a copy with one subtitle cue at 0:03: out_time never moved past it.
        var progress = new FFmpegProgress(durationMs: 2_400_000, sourceBytes: 194_433_003);
        foreach (var (size, time) in new[] { (13_369_392L, 3_000_000L), (104_595_504L, 3_000_000L), (171_442_224L, 3_000_000L) })
        {
            progress.Update($"total_size={size}");
            progress.Update($"out_time_us={time}");
        }

        Assert.AreEqual(88, progress.Percent);
    }

    [TestMethod]
    public void NeverGoesBackwardsOrPastAHundred()
    {
        var progress = new FFmpegProgress(durationMs: 100_000, totalFrames: 100, sourceBytes: 1000);

        progress.Update("frame=60");
        progress.Update("out_time_us=10000000");
        Assert.AreEqual(60, progress.Percent);

        progress.Update("total_size=5000");
        Assert.AreEqual(100, progress.Percent);
    }

    [TestMethod]
    public void IgnoresOtherLines()
    {
        var progress = new FFmpegProgress(durationMs: 100_000, totalFrames: 100);

        progress.Update("speed=1.5x");
        progress.Update("Stream #0:0 -> #0:0 (copy)");
        progress.Update("progress=continue");
        progress.Update("frame=");

        Assert.AreEqual(0, progress.Percent);
    }

    // The final report of a real copy: before, out_time ended at the subtitle's last cue
    // (0:03 of a minute), so the last progress ffmpeg gave was 5%.
    [TestMethod]
    public async Task Remux_WithASparseSubtitle_ReportsTheWholeCopy()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"muxarr_progress_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var srt = Path.Combine(dir, "one.srt");
            await File.WriteAllTextAsync(srt, "1\n00:00:01,000 --> 00:00:03,000\nHello\n\n");
            var input = Path.Combine(dir, "in.mp4");
            var made = await ProcessExecutor.ExecuteProcessAsync("ffmpeg",
                "-y -loglevel error -f lavfi -i testsrc2=s=160x90:r=24 -f lavfi -i sine=frequency=440:sample_rate=48000 " +
                $"-i \"{srt}\" -t 60 -map 0:v -map 1:a -map 2:s -c:v libx264 -preset ultrafast -c:a aac -c:s mov_text \"{input}\"",
                TimeSpan.FromSeconds(60));
            Assert.IsTrue(made.Success, made.Error);

            var seen = new List<int>();
            var result = await FFmpeg.Remux(input, Path.Combine(dir, "out.mp4"),
                TestPlan.Of(new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                    new TrackPlan { Index = 1, Type = MediaTrackType.Audio },
                    new TrackPlan { Index = 2, Type = MediaTrackType.Subtitles }),
                sourceDurationMs: 60_000, onProgress: (_, p) => seen.Add(p));

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsTrue(seen.Max() >= 95, $"last progress {seen.Max()}%");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
