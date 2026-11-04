using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Muxarr.Tests;

/// <summary>
/// ffmpeg cannot write the dvhe sample entry, so the Dolby Vision enhancement-layer track
/// of a dual-track MP4 comes out as hev1. The export finds those conversions in their logs.
/// </summary>
[TestClass]
public class RelabeledDolbyVisionTests
{
    // ffmpeg's stream listing from a real conversion of a DVDFab rip.
    private static readonly string[] DualLayerLog =
    [
        "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from '/data/media/bluray/Film (1996)/Film (1996).mp4':",
        "  Stream #0:0[0x1](und): Video: hevc (Main 10) (hev1 / 0x31766568), yuv420p10le(tv, bt2020nc/bt2020/smpte2084), 3840x2160 [SAR 1:1 DAR 16:9], 53679 kb/s, 23.98 fps, 23.98 tbr, 24k tbn (default)",
        "  Stream #0:1[0x2](und): Video: hevc (Main 10) (dvhe / 0x65687664), yuv420p10le(tv, bt2020nc/bt2020/smpte2084), 1920x1080 [SAR 1:1 DAR 16:9], 3940 kb/s, 23.98 fps, 23.98 tbr, 24k tbn (default)",
        "  Stream #0:2[0x3](eng): Audio: eac3 (ac3) (ac-3 / 0x332D6361), 48000 Hz, 5.1(side), fltp, 640 kb/s (default)",
        "  Stream #0:3[0x4](eng): Subtitle: dvd_subtitle (dvdsub) (mp4s / 0x7334706D), 9 kb/s (default)",
        "  Stream #0:4[0x5](eng): Subtitle: dvd_subtitle (dvdsub) (mp4s / 0x7334706D), 10 kb/s",
        "  Stream #0:5[0x6](eng): Subtitle: dvd_subtitle (dvdsub) (mp4s / 0x7334706D), 16 kb/s",
        "  Stream #0:6[0x7](und): Data: bin_data (text / 0x74786574)",
        "  Stream #0:7[0x0]: Video: mjpeg (Progressive), yuvj420p(pc, bt470bg/unknown/unknown), 500x750 [SAR 1:1 DAR 2:3], 90k tbr, 90k tbn (attached pic)",
        "  Stream #0:0 -> #0:0 (copy)",
        "  Stream #0:1 -> #0:1 (copy)",
        "  Stream #0:2 -> #0:2 (copy)",
        "  Stream #0:3 -> #0:3 (copy)",
        "  Stream #0:4 -> #0:4 (copy)",
        "  Stream #0:5 -> #0:5 (copy)",
        "  Stream #0:7 -> #0:6 (copy)",
        "Output #0, mp4, to '/data/media/bluray/Film (1996)/Film (1996).mp4.muxtmp':",
        "  Stream #0:0(und): Video: hevc (Main 10) (hev1 / 0x31766568), yuv420p10le(tv, bt2020nc/bt2020/smpte2084), 3840x2160 [SAR 1:1 DAR 16:9], q=2-31, 53679 kb/s, 23.98 fps, 23.98 tbr, 24k tbn (default)",
        "  Stream #0:1(und): Video: hevc (Main 10) (hev1 / 0x31766568), yuv420p10le(tv, bt2020nc/bt2020/smpte2084), 1920x1080 [SAR 1:1 DAR 16:9], q=2-31, 3940 kb/s, 23.98 fps, 23.98 tbr, 24k tbn (default)",
        "  Stream #0:2(eng): Audio: eac3 (ec-3 / 0x332D6365), 48000 Hz, 5.1(side), fltp, 640 kb/s (default)",
        "  Stream #0:3(eng): Subtitle: dvd_subtitle (mp4s / 0x7334706D), 720x480, 9 kb/s (default)",
        "  Stream #0:4(eng): Subtitle: dvd_subtitle (mp4s / 0x7334706D), 720x480, 10 kb/s",
        "  Stream #0:5(eng): Subtitle: dvd_subtitle (mp4s / 0x7334706D), 720x480, 16 kb/s",
        "  Stream #0:6: Video: mjpeg (Progressive) (mp4v / 0x7634706D), yuvj420p(pc, bt470bg/unknown/unknown), 500x750 [SAR 1:1 DAR 2:3], q=2-31, 90k tbr, 90k tbn (attached pic)"
    ];

    [TestMethod]
    public void DualLayer_EnhancementLayerTrackWentFromDvheToHev1()
    {
        CollectionAssert.AreEqual(new[] { "track 1: dvhe -> hev1" }, DiagnosticsService.RelabeledDolbyVisionTracks(DualLayerLog));
    }

    [TestMethod]
    public void KeptTag_IsNotReported()
    {
        var log = DualLayerLog.Select(l => l.Contains("Output #0") ? l : l).ToList();
        var output = log.FindIndex(l => l.StartsWith("Output #0"));
        log[output + 2] = log[output + 2].Replace("(hev1 / 0x31766568)", "(dvhe / 0x65687664)");

        Assert.AreEqual(0, DiagnosticsService.RelabeledDolbyVisionTracks(log).Count);
    }

    [TestMethod]
    public void TrackNotMapped_IsNotReported()
    {
        var log = DualLayerLog.Where(l => !l.Contains("Stream #0:1 -> #0:1")).ToList();

        Assert.AreEqual(0, DiagnosticsService.RelabeledDolbyVisionTracks(log).Count);
    }

    [TestMethod]
    public void OnlyTheLastRunCounts()
    {
        var fixedRun = DualLayerLog.Select(l => l.Replace("(hev1 / 0x31766568), yuv420p10le(tv, bt2020nc/bt2020/smpte2084), 1920x1080",
            "(dvhe / 0x65687664), yuv420p10le(tv, bt2020nc/bt2020/smpte2084), 1920x1080"));

        Assert.AreEqual(0, DiagnosticsService.RelabeledDolbyVisionTracks(DualLayerLog.Concat(fixedRun)).Count);
    }

    [TestMethod]
    public void NameRestoredAfterTheRun_IsNotReported()
    {
        var log = DualLayerLog.Append(
            "Filed output track 1 as 'dvhe' again: ffmpeg wrote 'hev1', a name players read as video without Dolby Vision.");

        Assert.AreEqual(0, DiagnosticsService.RelabeledDolbyVisionTracks(log).Count);
    }

    [TestMethod]
    public void NameRestoredOnAnotherTrack_StillReportsThisOne()
    {
        var log = DualLayerLog.Append(
            "Filed output track 0 as 'dvhe' again: ffmpeg wrote 'hev1', a name players read as video without Dolby Vision.");

        CollectionAssert.AreEqual(new[] { "track 1: dvhe -> hev1" }, DiagnosticsService.RelabeledDolbyVisionTracks(log));
    }

    [TestMethod]
    public async Task Report_ListsTheConversion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"muxarr_dv_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options;
        try
        {
            await using (var context = new AppDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                var profile = new Profile { Name = "Movies", Directories = ["/data/media/bluray"] };
                context.Profiles.Add(profile);
                var file = new MediaFile { Path = "/data/media/bluray/Film (1996)/Film (1996).mp4", Profile = profile };
                context.MediaFiles.Add(file);
                context.MediaConversions.Add(new MediaConversion
                {
                    Name = "Film", MediaFile = file, State = ConversionState.Completed,
                    LogEntries = DualLayerLog.Select(l => new ConversionLogEntry { Timestamp = DateTime.UtcNow, Message = l }).ToList()
                });
                context.MediaConversions.Add(new MediaConversion
                {
                    Name = "Plain", MediaFile = new MediaFile { Path = "/data/media/bluray/Plain (2020)/Plain (2020).mp4", Profile = profile },
                    State = ConversionState.Completed,
                    LogEntries = [new ConversionLogEntry { Timestamp = DateTime.UtcNow, Message = "Starting ffmpeg stream copy for Plain.." }]
                });
                await context.SaveChangesAsync();
            }

            var report = await new DiagnosticsService(new Factory(options)).BuildReportAsync();
            var section = report[report.IndexOf("== Past conversions that relabeled a Dolby Vision video track ==", StringComparison.Ordinal)..];
            section = section[..section.IndexOf("\n== ", 5, StringComparison.Ordinal)];

            StringAssert.Contains(section, "Film (Completed");
            StringAssert.Contains(section, "track 1: dvhe -> hev1");
            Assert.IsFalse(section.Contains("Plain"), section);
            StringAssert.Contains(section, "1 conversion(s).");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
