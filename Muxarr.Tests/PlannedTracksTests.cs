using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

/// <summary>
/// A queued or running conversion shows what it will keep instead of an empty result.
/// </summary>
[TestClass]
public class PlannedTracksTests
{
    [TestMethod]
    public void PlannedTracks_AreTheSourceTracksThePlanKeeps_InPlanOrder()
    {
        var conversion = new MediaConversion
        {
            Name = "Spider-Man",
            BeforeSnapshot = new MediaSnapshot { Tracks = [Video(0), Audio(1, "English"), Sub(2, "English"), Sub(3, "Czech"), Sub(4, "English", hi: true)] },
            ConversionPlan = TestPlan.Of(
                new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                new TrackPlan { Index = 1, Type = MediaTrackType.Audio },
                new TrackPlan { Index = 4, Type = MediaTrackType.Subtitles },
                new TrackPlan { Index = 2, Type = MediaTrackType.Subtitles })
        };

        CollectionAssert.AreEqual(new[] { 0, 1, 4, 2 }, conversion.PlannedTracks().Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void PlannedTracks_WithoutASourceSnapshot_IsEmpty()
    {
        Assert.AreEqual(0, new MediaConversion { Name = "x" }.PlannedTracks().Count);
    }
}
