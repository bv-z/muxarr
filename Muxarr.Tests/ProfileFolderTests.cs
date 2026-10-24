using Muxarr.Core.Extensions;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

/// <summary>
/// A profile's folder holds the files inside it, not the files of a folder whose name starts the same.
/// </summary>
[TestClass]
public class ProfileFolderTests
{
    private static readonly List<Profile> Profiles = [new() { Name = "TV", Directories = ["/data/media/tv"] }];

    [TestMethod]
    [DataRow("/data/media/tv/Severance/Season 01/S01E01.mkv", true)]
    [DataRow("/data/media/tv-kids/Bluey/Season 01/S01E01.mkv", false)]
    [DataRow("/data/media/tv4k/Severance/S01E01.mkv", false)]
    [DataRow("/data/media/TV/Severance/S01E01.mkv", true)] // the match was case-insensitive before, and still is
    public void BestCandidate_OnlyForFilesInsideTheFolder(string path, bool matches)
    {
        Assert.AreEqual(matches, Profiles.GetBestCandidate(path) != null);
    }

    [TestMethod]
    [DataRow("/data/media/tv/", "/data/media/tv/Show/S01E01.mkv", true)]
    [DataRow("/data/media/tv", "/data/media/tv", true)]
    [DataRow("/data/media/tv", "/data/media/tvshows/S01E01.mkv", false)]
    [DataRow("/", "/data/media/tv/S01E01.mkv", true)]
    public void IsInDirectory(string directory, string path, bool expected)
    {
        Assert.AreEqual(expected, path.IsInDirectory(directory));
    }
}
