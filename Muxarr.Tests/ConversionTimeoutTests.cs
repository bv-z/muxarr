using Muxarr.Core.Config;
using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

/// <summary>
/// The settings page says a conversion timeout of 0 means no limit.
/// </summary>
[TestClass]
public class ConversionTimeoutTests
{
    [TestMethod]
    public void Zero_MeansNoLimit()
    {
        Assert.AreEqual(Timeout.InfiniteTimeSpan, new ProcessingConfig { ConversionTimeoutMinutes = 0 }.ConversionTimeout);
    }

    [TestMethod]
    public void Minutes_AreTheLimit()
    {
        Assert.AreEqual(TimeSpan.FromMinutes(90), new ProcessingConfig { ConversionTimeoutMinutes = 90 }.ConversionTimeout);
    }

    [TestMethod]
    public async Task ProcessExecutor_RunsToTheEndWithNoLimit()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Uses /bin/sh");
        }

        var result = await ProcessExecutor.ExecuteProcessAsync("/bin/sh", "-c \"sleep 0.2; echo done\"", Timeout.InfiniteTimeSpan);

        Assert.IsFalse(result.TimedOut);
        Assert.AreEqual(0, result.ExitCode);
        StringAssert.Contains(result.Output, "done");
    }
}
