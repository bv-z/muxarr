using Muxarr.Core.Extensions;
using Muxarr.Core.FFmpeg;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;

namespace Muxarr.Tests;

[TestClass]
public class FFmpegHelperTests
{
    [TestMethod]
    public void BuildDispositionValue_AllFlagsNull_ReturnsNull()
    {
        var track = new TrackPlan { Index = 0, Type = MediaTrackType.Audio };

        Assert.IsNull(FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_SingleFlagTrue_EmitsPositive()
    {
        var track = new TrackPlan
        {
            Index = 1,
            Type = MediaTrackType.Audio,
            IsDefault = true
        };

        Assert.AreEqual("+default", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_SingleFlagFalse_EmitsNegative()
    {
        var track = new TrackPlan
        {
            Index = 1,
            Type = MediaTrackType.Audio,
            IsDefault = false
        };

        Assert.AreEqual("-default", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_MultipleFlags_ConcatenatesInCanonicalOrder()
    {
        var track = new TrackPlan
        {
            Index = 2,
            Type = MediaTrackType.Subtitles,
            IsDefault = true,
            IsForced = true,
            IsHearingImpaired = false,
            IsCommentary = false
        };

        Assert.AreEqual("+default+forced-hearing_impaired-comment", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    [DataRow(nameof(TrackPlan.IsDefault), true, "+default")]
    [DataRow(nameof(TrackPlan.IsDefault), false, "-default")]
    [DataRow(nameof(TrackPlan.IsForced), true, "+forced")]
    [DataRow(nameof(TrackPlan.IsHearingImpaired), true, "+hearing_impaired")]
    [DataRow(nameof(TrackPlan.IsVisualImpaired), true, "+visual_impaired")]
    [DataRow(nameof(TrackPlan.IsVisualImpaired), false, "-visual_impaired")]
    [DataRow(nameof(TrackPlan.IsCommentary), true, "+comment")]
    [DataRow(nameof(TrackPlan.IsOriginal), true, "+original")]
    [DataRow(nameof(TrackPlan.IsOriginal), false, "-original")]
    [DataRow(nameof(TrackPlan.IsDub), true, "+dub")]
    public void BuildDispositionValue_AllSupportedFlags(string fieldName, bool value, string expected)
    {
        var track = new TrackPlan { Index = 1, Type = MediaTrackType.Audio };
        typeof(TrackPlan).GetProperty(fieldName)!.SetValue(track, (bool?)value);

        Assert.AreEqual(expected, FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_CommentaryMapsToFfmpegCommentFlag()
    {
        var track = new TrackPlan
        {
            Index = 3,
            Type = MediaTrackType.Audio,
            IsCommentary = true
        };

        Assert.AreEqual("+comment", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void EscapeValue_WrapsInQuotes()
    {
        Assert.AreEqual("\"hello\"", FFmpegHelper.EscapeValue("hello"));
    }

    [TestMethod]
    public void EscapeValue_EscapesInnerQuotes()
    {
        Assert.AreEqual("\"He said \\\"hi\\\"\"", FFmpegHelper.EscapeValue("He said \"hi\""));
    }

    [TestMethod]
    public void EscapeValue_LeavesBackslashesThatAreNotBeforeAQuote()
    {
        Assert.AreEqual("\"a\\b\"", FFmpegHelper.EscapeValue("a\\b"));
    }

    [TestMethod]
    public void ToContainerFamily_Matroska()
    {
        Assert.AreEqual(ContainerFamily.Matroska, "Matroska".ToContainerFamily());
        Assert.AreEqual(ContainerFamily.Matroska, "WebM".ToContainerFamily());
    }

    [TestMethod]
    public void ToContainerFamily_Mp4_BothMkvmergeVariants()
    {
        Assert.AreEqual(ContainerFamily.Mp4, "QuickTime/MP4".ToContainerFamily());
        Assert.AreEqual(ContainerFamily.Mp4, "MP4/QuickTime".ToContainerFamily());
    }

    [TestMethod]
    public void ToContainerFamily_UnknownOrNull()
    {
        Assert.AreEqual(ContainerFamily.Unknown, ((string?)null).ToContainerFamily());
        Assert.AreEqual(ContainerFamily.Unknown, "".ToContainerFamily());
        Assert.AreEqual(ContainerFamily.Unknown, "AVI".ToContainerFamily());
    }
}
