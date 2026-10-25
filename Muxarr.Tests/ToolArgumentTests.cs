using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

/// <summary>
/// Paths, titles and track names reach mkvmerge, mkvpropedit, ffmpeg and ffprobe exactly as written.
/// </summary>
[TestClass]
public class ToolArgumentTests
{
    [TestMethod]
    [DataRow("The \"Best\" Movie (2020).mkv")]
    [DataRow("AC\\DC Live")]
    [DataRow("ends with a backslash\\")]
    [DataRow("a\\\"b")]
    [DataRow("Ca$h `uname` (2010)")]
    [DataRow("Ocean's Eleven")]
    [DataRow("Amélie")]
    [DataRow("")]
    public async Task EscapeValue_RoundTripsThroughTheCommandLine(string value)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Uses printf");
        }

        var result = await ProcessExecutor.ExecuteProcessAsync("printf",
            $"\"[%s]\" {MkvToolNixHelper.EscapeValue(value)}");

        Assert.AreEqual($"[{value}]", result.Output.TrimEnd('\n', '\r'));
    }
}
