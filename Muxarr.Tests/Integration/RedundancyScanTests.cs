using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Language;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

[TestClass]
public class RedundancyScanTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Scan_FlagsSubtitlesToRemove_AndConversionClearsIt()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();
        profile = await Fixture.WithDbContext(async ctx =>
        {
            var p = await ctx.Profiles.SingleAsync(x => x.Id == profile.Id);
            p.SubtitleSettings = new TrackSettings { Enabled = true, AllowedLanguages = [IsoLanguage.Find("English")] };
            await ctx.SaveChangesAsync();
            return p;
        });

        var file = await Fixture.ScanAndPersist(path, profile);
        Assert.IsTrue(file.HasRedundantSubtitles, "French and Spanish subtitles are to go");
        Assert.IsFalse(file.HasRedundantAudio, "audio removal is off");

        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromProfile(profile), false);
        await Fixture.Converter.RunAsync(CancellationToken.None);
        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        var after = await Fixture.ScanAndPersist(path, profile);
        Assert.IsFalse(after.HasRedundantSubtitles || after.HasRedundantAudio || after.HasRedundantTracks);
    }
}
