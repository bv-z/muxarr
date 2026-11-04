using Muxarr.Core.FFmpeg;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests.Integration;

/// <summary>
/// Dolby Vision profile 5 in an MP4, filed under dvhe. ffmpeg cannot
/// write dvhe, so a remux refiled it as hev1 with its Dolby Vision settings still inside.
/// The name is now put back after ffmpeg finishes, and any output still renamed is refused.
/// </summary>
[TestClass]
public class DolbyVisionSampleEntryTests : IntegrationTestBase
{
    [TestMethod]
    public async Task DvheMp4_ConvertsAndKeepsItsName()
    {
        var path = CopyFixture("dv_p5_dvhe.mp4");
        var hash = FileAssertions.Sha256(path);
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);
        var tracks = file.Snapshot.Tracks.ToSnapshots();
        tracks.Single(t => t.Type == MediaTrackType.Audio).Name = "English";
        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromCustom(tracks), true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var done = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        FileAssertions.AssertSha256NotEquals(path, hash, "the converted copy replaces the original");
        var probe = (await FFmpeg.GetStreamInfo(path)).Result!;
        var video = probe.Streams.Single(s => s.CodecType == "video");
        Assert.AreEqual("dvhe", video.CodecTagString, done.LogText());
        Assert.IsNotNull(video.DolbyVisionConfig, "the Dolby Vision settings stay inside");
        Assert.AreEqual("English", (await FileAssertions.ProbeAsync(path)).Snapshot.Tracks
            .Single(t => t.Type == MediaTrackType.Audio).Name, "the edit itself applied");
        StringAssert.Contains(done.LogText(), "Filed output track 0 as 'dvhe' again: ffmpeg wrote 'hev1'");
        Assert.AreEqual(0, DiagnosticsService.RelabeledDolbyVisionTracks(done.LogEntries.OrderBy(e => e.Id).Select(e => e.Message)).Count,
            "the export must not list it as relabeled");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    public void Validator_RefusesARenamedDolbyVisionTrack()
    {
        var plan = TestPlan.Of(new TrackPlan { Index = 0, Type = MediaTrackType.Video },
            new TrackPlan { Index = 1, Type = MediaTrackType.Audio });

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateDolbyVisionOrThrow(Probe("dvh1"), Probe("hvc1"), plan));
        StringAssert.Contains(ex.Message, "'hvc1' instead of 'dvh1'");
    }

    [TestMethod]
    [DataRow("dvh1", "dvh1", DisplayName = "MP4 that keeps its name")]
    [DataRow("hvc1", "hev1", DisplayName = "Profile 8 filed as plain HEVC from the start")]
    [DataRow("[0][0][0][0]", "[0][0][0][0]", DisplayName = "Matroska, which has no such names")]
    public void Validator_AcceptsWhatPlayersSeeTheSame(string before, string after)
    {
        var plan = TestPlan.Of(new TrackPlan { Index = 0, Type = MediaTrackType.Video },
            new TrackPlan { Index = 1, Type = MediaTrackType.Audio });

        OutputValidator.ValidateDolbyVisionOrThrow(Probe(before), Probe(after), plan);
    }

    private static FFprobeResult Probe(string videoTag)
    {
        return new FFprobeResult
        {
            Streams =
            [
                new FFprobeStream
                {
                    Index = 0, CodecType = "video", CodecName = "hevc", CodecTagString = videoTag,
                    SideDataList = [new FFprobeSideData { SideDataType = FFprobeSideData.DolbyVisionConfigType, DvProfile = 5 }]
                },
                new FFprobeStream { Index = 1, CodecType = "audio", CodecName = "aac", CodecTagString = "mp4a" }
            ]
        };
    }
}
