using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

/// <summary>
/// File and track names with quotes and backslashes convert like any other.
/// </summary>
[TestClass]
public class OddNameTests : IntegrationTestBase
{
    private const string TrackName = "Director's \"Cut\" \\ Commentary";

    [TestMethod]
    [DataRow("test_complex.mkv", "The \"Best\" Movie (2020).mkv")]
    [DataRow("test_complex.mp4", "The \"Best\" Movie (2020).mp4")]
    public async Task QuoteInTheFileName_ScansAndConverts(string fixture, string name)
    {
        var path = CopyFixture(fixture, name);
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);
        Assert.IsTrue(file.Snapshot.Tracks.Count > 2, "the scan should read the tracks");

        var tracks = file.Snapshot.Tracks.ToSnapshots();
        var dropped = tracks.Last(t => t.Type != MediaTrackType.Video);
        tracks.Remove(dropped);
        tracks.First(t => t.Type == MediaTrackType.Audio).Name = TrackName;
        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromCustom(tracks), true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        var probed = await FileAssertions.ProbeAsync(path);
        Assert.AreEqual(TrackName, probed.Snapshot.Tracks.First(t => t.Type == MediaTrackType.Audio).Name);
        FileAssertions.AssertNoStrayArtifacts(TempDir, name);
    }
}
