using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Config;
using Muxarr.Core.Language;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests;

/// <summary>
/// The exported diagnostics file has what is needed to find a problem, and no secrets.
/// </summary>
[TestClass]
public class DiagnosticsServiceTests
{
    private const string WebhookKey = "a8125cfff97846849b0bdabbfa53379b";
    private const string RadarrKey = "radarr0123456789abcdef0123456789";
    private const string PasswordHash = "AQAAAAIAAYagAAAAEIE2OVgXvQAwu4OI4qAaN";
    private const string DiscordHook = "https://discord.com/api/webhooks/123/TopSecretWebhookToken";
    private const string PlexToken = "AbCdEfGhIjKlMnOpQrSt";

    private string _dbPath = null!;
    private DbContextOptions<AppDbContext> _options = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"muxarr_diag_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        await using var context = new AppDbContext(_options);
        await context.Database.EnsureCreatedAsync();

        context.Configs.Set(new WebhookConfig { ApiKey = WebhookKey, MuxarrUrl = "http://tower:8183" });
        context.Configs.Set(new AuthConfig { Username = "brett", PasswordHash = PasswordHash }, AuthConfig.Key);
        context.Configs.Set(new ProcessingConfig
        {
            PostProcessingEnabled = true,
            PostProcessingCommand = $"curl -s 'http://plex:32400/library/sections/1/refresh?X-Plex-Token={PlexToken}'"
        });
        context.Configs.Set(new List<NotificationConfig>
        {
            new() { Name = "Discord", Provider = "Discord", Settings = new() { ["WebhookUrl"] = DiscordHook } }
        });
        context.Integrations.Add(new Muxarr.Data.Entities.Integration { Name = "Radarr", Type = IntegrationType.Radarr, Url = "http://radarr:7878", ApiKey = RadarrKey });
        var profile = new Profile
        {
            Name = "Movies & TV",
            Directories = ["/data/media/bluray"],
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.OriginalLanguage, IsoLanguage.Find("English")]
            }
        };
        context.Profiles.Add(profile);
        var file = new MediaFile { Path = "/data/media/bluray/Unknown Film (2021)/Unknown Film (2021).mkv", Profile = profile };
        context.MediaFiles.Add(file);
        context.MediaConversions.Add(new MediaConversion
        {
            Name = "Unknown Film", MediaFile = file, State = ConversionState.Failed,
            Log = "[2026-10-24 18:41:28.400] Something bad happened while processing the file."
        });
        context.LogEntries.Add(new LogEntry
        {
            Timestamp = DateTime.UtcNow.AddMinutes(-5), Level = "ERR", Source = "MediaConverterService",
            Message = "Conversion state for Big Remux is in progress on startup.", Exception = "System.Exception: boom\n   at Somewhere()"
        });
        context.LogEntries.Add(new LogEntry
        {
            Timestamp = DateTime.UtcNow.AddDays(-30), Level = "INF", Source = "Old", Message = "a month ago"
        });
        await context.SaveChangesAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        using var context = new AppDbContext(_options);
        context.Database.EnsureDeleted();
    }

    [TestMethod]
    public async Task Report_HasWhatIsNeeded()
    {
        var report = await new DiagnosticsService(new Factory(_options)).BuildReportAsync();

        StringAssert.Contains(report, "Profile \"Movies & TV\"");
        StringAssert.Contains(report, "keep Original Language, English");
        StringAssert.Contains(report, "Radarr \"Radarr\": http://radarr:7878, API key set");
        StringAssert.Contains(report, "Without an original language from Sonarr/Radarr: 1");
        StringAssert.Contains(report, "/data/media/bluray/Unknown Film (2021)/Unknown Film (2021).mkv");
        StringAssert.Contains(report, "Something bad happened while processing the file.");
        StringAssert.Contains(report, "Conversion state for Big Remux is in progress on startup.");
        StringAssert.Contains(report, "System.Exception: boom");
        StringAssert.Contains(report, "curl -s 'http://plex:32400/library/sections/1/refresh?X-Plex-Token=[redacted]'");
        Assert.IsFalse(report.Contains("a month ago"), "only the last days of the log");
    }

    [TestMethod]
    public async Task Report_LeavesOutSecrets()
    {
        var report = await new DiagnosticsService(new Factory(_options)).BuildReportAsync();

        foreach (var secret in new[] { WebhookKey, RadarrKey, PasswordHash, DiscordHook, "TopSecretWebhookToken", PlexToken })
        {
            Assert.IsFalse(report.Contains(secret), $"the report must not contain {secret}");
        }
    }

    [TestMethod]
    public async Task Report_TellsAReplacedFileFromOneTheConversionDidNotFinish()
    {
        await using (var context = new AppDbContext(_options))
        {
            var profile = await context.Profiles.SingleAsync();
            profile.SubtitleSettings = new TrackSettings { Enabled = true, AllowedLanguages = [IsoLanguage.Find("English")] };
            foreach (var (title, sizeAfter) in new[] { ("Upgraded Film", 1000L), ("Stuck Film", 2000L) })
            {
                var snapshot = new MediaSnapshot
                {
                    Tracks =
                    [
                        new TrackSnapshot { Index = 0, Type = Muxarr.Core.Models.MediaTrackType.Video, Codec = "hevc" },
                        new TrackSnapshot { Index = 1, Type = Muxarr.Core.Models.MediaTrackType.Audio, LanguageName = "English", LanguageCode = "eng", Codec = "eac3" },
                        new TrackSnapshot { Index = 2, Type = Muxarr.Core.Models.MediaTrackType.Subtitles, LanguageName = "English", LanguageCode = "eng", Codec = "srt" },
                        new TrackSnapshot { Index = 3, Type = Muxarr.Core.Models.MediaTrackType.Subtitles, LanguageName = "Czech", LanguageCode = "cze", Codec = "srt" }
                    ]
                };
                var file = new MediaFile
                {
                    Path = $"/data/media/bluray/{title}/{title}.mkv", Title = title, OriginalLanguage = "English",
                    Profile = profile, Snapshot = snapshot, HasRedundantTracks = true, Size = 2000,
                    FileLastWriteTime = DateTime.UtcNow.AddDays(-2)
                };
                context.MediaFiles.Add(file);
                context.MediaConversions.Add(new MediaConversion
                {
                    Name = title, MediaFile = file, State = ConversionState.Completed, SizeAfter = sizeAfter,
                    UpdatedDate = DateTime.UtcNow.AddDays(-1)
                });
            }

            await context.SaveChangesAsync();
        }

        var report = await new DiagnosticsService(new Factory(_options)).BuildReportAsync();

        StringAssert.Contains(report, "Converted, then replaced: 1. Converted and unchanged, yet still needing work: 1.");
        StringAssert.Contains(report, "Would remove now:     s#3 Czech");
        var stuck = report[report.IndexOf("Stuck Film.mkv", StringComparison.Ordinal)..];
        StringAssert.Contains(stuck[..stuck.IndexOf("Original language", StringComparison.Ordinal)], "unchanged, so the conversion did not reach its target");
    }

    [TestMethod]
    public void FileName_IsDatedText()
    {
        StringAssert.Matches(DiagnosticsService.FileName(DateTime.UtcNow),
            new System.Text.RegularExpressions.Regex(@"^muxarr-diagnostics-\d{4}-\d{2}-\d{2}-\d{4}\.txt$"));
    }

    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            return new AppDbContext(options);
        }
    }
}
