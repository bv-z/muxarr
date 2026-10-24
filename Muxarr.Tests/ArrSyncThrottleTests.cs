using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Muxarr.Core.Api;
using Muxarr.Core.Api.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests;

/// <summary>
/// The sync is held off for a few minutes after it ran, but only after it actually read an
/// arr: the first start runs it before Sonarr and Radarr are set up, and the scan right after
/// setup must still get the original languages.
/// </summary>
[TestClass]
public class ArrSyncThrottleTests
{
    private string _dbPath = null!;
    private DbContextOptions<AppDbContext> _dbOptions = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"muxarr_arrthrottle_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureDeleted();
    }

    [TestMethod]
    public async Task Sync_BeforeSetup_DoesNotHoldOffTheSyncAfterSetup()
    {
        var radarr = new StubHandler(HttpStatusCode.OK, MoviesJson);
        await RunSync(radarr); // first start: nothing set up yet

        await AddRadarr();
        await RunSync(radarr); // the scan after setup

        await using var context = new AppDbContext(_dbOptions);
        var stored = await context.MediaInfos.SingleAsync();
        Assert.AreEqual("Korean", stored.OriginalLanguage);
    }

    [TestMethod]
    public async Task Sync_ThatReachedTheArr_HoldsOffTheNextOne()
    {
        await AddRadarr();
        var radarr = new StubHandler(HttpStatusCode.OK, MoviesJson);
        await RunSync(radarr);
        await RunSync(radarr);

        Assert.AreEqual(1, radarr.Requests);
    }

    [TestMethod]
    public async Task Sync_ThatCouldNotReadTheArr_KeepsWhatItKnewAndRetries()
    {
        await AddRadarr();
        await RunSync(new StubHandler(HttpStatusCode.OK, MoviesJson));
        await ClearLastSync();

        var down = new StubHandler(HttpStatusCode.Unauthorized, "");
        await RunSync(down);
        await RunSync(down);

        await using var context = new AppDbContext(_dbOptions);
        Assert.AreEqual(1, await context.MediaInfos.CountAsync(), "A failed read must not empty what Muxarr knew.");
        Assert.AreEqual(2, down.Requests, "A failed read must not hold off the next try.");
    }

    [TestMethod]
    public async Task SeriesSync_WithAnEpisodeListThatFails_KeepsWhatItKnew()
    {
        await using (var seed = new AppDbContext(_dbOptions))
        {
            seed.Integrations.Add(new Muxarr.Data.Entities.Integration { Name = "Sonarr", Type = IntegrationType.Sonarr, Url = "http://sonarr:8989", ApiKey = "key" });
            await seed.SaveChangesAsync();
            seed.MediaInfos.Add(new MediaInfo
            {
                ExternalId = 7, IntegrationId = (await seed.Integrations.SingleAsync()).Id, Title = "Show S01E01",
                OriginalLanguage = "Korean", Path = "/data/tv/Show/S01E01.mkv"
            });
            await seed.SaveChangesAsync();
        }

        var series = JsonHelper.Serialize(new List<SeriesResponse> { new() { Id = 1, Title = "Show", Path = "/data/tv/Show" } });
        await RunSync(new StubHandler(HttpStatusCode.OK, series, failWhen: "/episode"));

        await using var context = new AppDbContext(_dbOptions);
        Assert.AreEqual("Korean", (await context.MediaInfos.SingleAsync()).OriginalLanguage);
    }

    private static string MoviesJson => JsonHelper.Serialize(new List<MovieResponse>
    {
        new()
        {
            Id = 1, Title = "Parasite", OriginalLanguage = new Language { Name = "Korean" },
            MovieFile = new MovieFile { Id = 11, Path = "/data/movies/Parasite (2019)/Parasite (2019).mkv" }
        }
    });

    private async Task AddRadarr()
    {
        await using var seed = new AppDbContext(_dbOptions);
        seed.Integrations.Add(new Muxarr.Data.Entities.Integration { Name = "Radarr", Type = IntegrationType.Radarr, Url = "http://radarr:7878", ApiKey = "key" });
        await seed.SaveChangesAsync();
    }

    private async Task ClearLastSync()
    {
        await using var context = new AppDbContext(_dbOptions);
        context.Configs.Set(DateTime.MinValue, "LastArrSync");
        await context.SaveChangesAsync();
    }

    private async Task RunSync(StubHandler handler)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        await using var provider = services.BuildServiceProvider();

        var arrApi = new ArrApiClient(NullLogger<ArrApiClient>.Instance, new StubHttpClientFactory(handler));
        var service = new ArrSyncService(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ArrSyncService>.Instance, arrApi);
        await service.RunAsync(CancellationToken.None);
    }

    private sealed class StubHttpClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, false);
        }
    }

    private sealed class StubHandler(HttpStatusCode status, string json, string? failWhen = null) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            var code = failWhen != null && request.RequestUri!.PathAndQuery.Contains(failWhen)
                ? HttpStatusCode.InternalServerError
                : status;
            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
