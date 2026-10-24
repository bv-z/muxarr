using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

/// <summary>
/// A folder Muxarr may not read, such as a root-owned 0700 one, must not end a scan or the
/// startup cleanup for everything else in the library.
/// </summary>
[TestClass]
public class UnreadableFolderTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"muxarr_unreadable_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in Directory.EnumerateDirectories(_root, "*", SearchOption.TopDirectoryOnly))
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(_root, true);
    }

    [TestMethod]
    public void EnumerateFilesRecursive_SkipsAFolderItMayNotRead()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Inconclusive("Needs Unix permissions that apply to the current user (not root).");
        }

        var readable = Path.Combine(_root, "Movie (2020)");
        var locked = Path.Combine(_root, "Locked");
        Directory.CreateDirectory(readable);
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(readable, "Movie (2020).mkv.muxbak"), "");
        File.WriteAllText(Path.Combine(locked, "Other.mkv.muxbak"), "");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        var found = FileHelper.EnumerateFilesRecursive(_root, "*.muxbak").ToList();

        CollectionAssert.AreEqual(new[] { Path.Combine(readable, "Movie (2020).mkv.muxbak") }, found);
    }

    [TestMethod]
    public void EnumerateFilesRecursive_FindsWhatTheSearchOptionOverloadFinds()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Show", "Season 01"));
        Directory.CreateDirectory(Path.Combine(_root, ".hidden"));
        foreach (var f in new[] { "Show/Season 01/S01E01.mkv", ".hidden/clip.mp4", ".dotfile.mkv", "noext", "a.b.c" })
        {
            File.WriteAllText(Path.Combine(_root, f), "");
        }

        var expected = Directory.EnumerateFiles(_root, "*.*", SearchOption.AllDirectories).Order().ToList();
        var found = FileHelper.EnumerateFilesRecursive(_root, "*.*").Order().ToList();

        CollectionAssert.AreEqual(expected, found);
        Assert.AreEqual(5, found.Count);
    }
}
