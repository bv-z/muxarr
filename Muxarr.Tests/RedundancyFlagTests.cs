using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

/// <summary>
/// The library tells audio that would be removed apart from subtitles that would be.
/// </summary>
[TestClass]
public class RedundancyFlagTests
{
    private static MediaFile File(params TrackSnapshot[] tracks)
    {
        return MakeFile("English", [Video(0), .. tracks]);
    }

    private static Profile Profile(bool subtitles = true) => new()
    {
        AudioSettings = new TrackSettings { Enabled = true, AllowedLanguages = [IsoLanguage.Find("English")] },
        SubtitleSettings = new TrackSettings { Enabled = subtitles, AllowedLanguages = [IsoLanguage.Find("English")] }
    };

    [TestMethod]
    public void OnlySubtitlesToRemove_FlagsSubtitlesNotAudio()
    {
        var file = File(Audio(1, "English"), Sub(2, "English"), Sub(3, "Czech"), Sub(4, "Danish"));
        var target = file.BuildTargetFromProfile(Profile());

        Assert.IsFalse(file.RemovesTracks(target, MediaTrackType.Audio));
        Assert.IsTrue(file.RemovesTracks(target, MediaTrackType.Subtitles));
    }

    [TestMethod]
    public void OnlyAudioToRemove_FlagsAudioNotSubtitles()
    {
        var file = File(Audio(1, "English"), Audio(2, "French"), Sub(3, "English"));
        var target = file.BuildTargetFromProfile(Profile());

        Assert.IsTrue(file.RemovesTracks(target, MediaTrackType.Audio));
        Assert.IsFalse(file.RemovesTracks(target, MediaTrackType.Subtitles));
    }

    [TestMethod]
    public void SubtitleRemovalOff_FlagsNoSubtitles()
    {
        var file = File(Audio(1, "English"), Sub(2, "Czech"));
        var target = file.BuildTargetFromProfile(Profile(subtitles: false));

        Assert.IsFalse(file.RemovesTracks(target, MediaTrackType.Subtitles));
    }

    [TestMethod]
    public void Filters_SplitAudioAndSubtitles()
    {
        var files = new List<MediaFile>
        {
            new() { Path = "audio", HasRedundantTracks = true, HasRedundantAudio = true },
            new() { Path = "subs", HasRedundantTracks = true, HasRedundantSubtitles = true },
            new() { Path = "both", HasRedundantTracks = true, HasRedundantAudio = true, HasRedundantSubtitles = true },
            new() { Path = "clean" }
        }.AsQueryable();

        CollectionAssert.AreEqual(new[] { "audio", "both" }, files.ApplyFileFilter("audio").Select(f => f.Path).ToArray());
        CollectionAssert.AreEqual(new[] { "subs", "both" }, files.ApplyFileFilter("subtitles").Select(f => f.Path).ToArray());
        CollectionAssert.AreEqual(new[] { "audio", "subs", "both" }, files.ApplyFileFilter("tracks").Select(f => f.Path).ToArray());
    }
}
