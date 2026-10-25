using Muxarr.Core.Config;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

/// <summary>
/// Post-processing runs "after each successful conversion", the in-place metadata edits included.
/// </summary>
[TestClass]
public class PostProcessingRunTests : IntegrationTestBase
{
    [TestMethod]
    public async Task MetadataOnlyEdit_RunsPostProcessing()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Uses /bin/sh");
        }

        var marker = Path.Combine(TempDir, "post-processing-ran");
        await Fixture.WithDbContext(async context =>
        {
            context.Configs.Set(new ProcessingConfig
            {
                PostProcessingEnabled = true,
                PostProcessingCommand = $"touch {marker}"
            });
            return await context.SaveChangesAsync();
        });

        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);
        var tracks = file.Snapshot.Tracks.ToSnapshots();
        tracks.First(t => t.Type == MediaTrackType.Audio).Name = "Renamed";
        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromCustom(tracks), true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        StringAssert.Contains(result.Log, "mkvpropedit", "this should be the in-place edit, not a remux");
        Assert.IsTrue(File.Exists(marker), "post-processing should have run");
    }
}
