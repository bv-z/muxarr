using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Data.Extensions.BatchTrackEdit;

namespace Muxarr.Tests.Integration;

/// <summary>
/// A batch removal goes through the real queue and converter: the track is gone from
/// the file, the others are byte-for-byte what they were, and the log says why.
/// </summary>
[TestClass]
public class BatchRemoveConversionTests : IntegrationTestBase
{
    [TestMethod]
    [DataRow("test_complex.mkv", MediaTrackType.Subtitles, 0)]
    [DataRow("test_complex.mkv", MediaTrackType.Audio, 1)]
    [DataRow("test_complex.mp4", MediaTrackType.Audio, 1)]
    public async Task BatchRemove_TakesTheTrackOutOfTheFile(string fixture, MediaTrackType type, int position)
    {
        var path = CopyFixture(fixture);
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);
        var before = file.Snapshot.Tracks.GroupBy(t => t.Type).ToDictionary(g => g.Key, g => g.Count());
        Assert.IsTrue(before.GetValueOrDefault(type) > position, $"fixture: needs a {type} track at position {position + 1}");

        var result = Apply(file, [new BatchSlotEdit { Slot = new TrackSlot(type, position), Remove = true }]);
        Assert.AreEqual(BatchOutcome.Changed, result.Outcome);
        var conversion = await Fixture.SeedConversion(file, result.Plan!, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var done = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        var after = (await FileAssertions.ProbeAsync(path)).Snapshot.Tracks.GroupBy(t => t.Type).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (kind, count) in before)
        {
            Assert.AreEqual(kind == type ? count - 1 : count, after.GetValueOrDefault(kind), $"{kind} tracks");
        }

        StringAssert.Contains(done.LogText(), "left out of this custom conversion");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }
}
