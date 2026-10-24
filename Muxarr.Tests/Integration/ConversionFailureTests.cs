using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests.Integration;

/// <summary>
/// Whatever goes wrong while a conversion is handled, it ends Failed rather than stuck.
/// </summary>
[TestClass]
public class ConversionFailureTests : IntegrationTestBase
{
    [TestMethod]
    public async Task ErrorDuringTheInPlaceEdit_FailsTheConversion()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);
        var targetTracks = file.Snapshot.Tracks.ToSnapshots();
        targetTracks.First(t => t.Type == MediaTrackType.Audio).Name = "Renamed";
        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromCustom(targetTracks), true);

        // Something fails right after the edit is marked as running (here a listener; in the
        // field a busy database or a failed rescan).
        var thrown = false;
        void OnChanged(ConverterProgressEvent e)
        {
            if (!thrown && e.Conversion.Id == conversion.Id && e.Conversion.State == ConversionState.Processing)
            {
                thrown = true;
                throw new InvalidOperationException("boom");
            }
        }

        Fixture.Converter.ConverterStateChanged += OnChanged;
        try
        {
            await Fixture.Converter.RunAsync(CancellationToken.None);
        }
        finally
        {
            Fixture.Converter.ConverterStateChanged -= OnChanged;
        }

        Assert.IsTrue(thrown, "the failure was never injected");
        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Failed);
        StringAssert.Contains(result.Log, "boom");
    }
}
