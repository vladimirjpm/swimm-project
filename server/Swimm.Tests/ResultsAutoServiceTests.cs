using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тик results-auto (П5) целиком: выбор строки → НАСТОЯЩИЙ <see cref="BulkPullService.PullOneAsync"/>
/// (путь пакетной кнопки и её классификатор) → гейты → вердикт в строке «Входящих». Сеть подменена:
/// превью и регламент — фейки, справочник бассейнов — в памяти. База — InMemory; CHECK вердикта
/// проверяет <see cref="ResultsAutoPgTests"/> на настоящем Postgres.
/// </summary>
public class ResultsAutoServiceTests
{
    // 20.11.2026 18:00 UTC = 20:00 по Израилю — вечер дня окончания.
    private static readonly DateTime Now = new(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Today = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

    private const string FinishedJson =
        """[{"event":"50 חופשי בנים","heat":1,"time":"0:30.12"},{"event":"50 חופשי בנים","heat":2,"time":"0:29.80"}]""";

    // Второе событие ещё не плыли — PDF идущего старта печатает его строками без времени.
    private const string RunningJson =
        """[{"event":"50 חופשי בנים","heat":1,"time":"0:30.12"},{"event":"100 גב בנות","heat":1,"time":"","time_fail":true}]""";

    private sealed class FixedTime(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    private sealed class Settings(string? mode) : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) =>
            key == ResultsAutoModes.SettingKey && mode is not null ? new AdminSetting(key, mode, "string", "admin", "") : null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    /// <summary>«Входящие» прямо из базы теста; MatchedCompetitionId — по OrgCompId соревнований.</summary>
    private sealed class DbDiscovery(SwimmDbContext db) : ICompetitionDiscoveryService
    {
        public async Task<IReadOnlyList<DiscoveredCompetitionDto>> GetAllAsync(CancellationToken ct = default)
        {
            var linked = await db.Competitions.Where(c => c.OrgCompId != null).Select(c => new { c.OrgCompId, c.Id }).ToListAsync(ct);
            return (await db.DiscoveredCompetitions.AsNoTracking().ToListAsync(ct))
                .Select(d => new DiscoveredCompetitionDto(d.Id, d.OrgCompId, d.Name, d.DateStart, d.DateEnd, d.Venue,
                    d.LogligId, d.Status, d.DiscoveredAt, d.LastSeenAt, d.LastError, null,
                    linked.FirstOrDefault(l => l.OrgCompId == d.OrgCompId)?.Id, d.Languages))
                .ToList();
        }

        public Task<DiscoverySyncResult> SyncAsync(int? year = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DiscoveredCompetitionDto?> RefreshDetailsAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(int Checked, int Resolved)> RefreshUpcomingDetailsAsync(int daysAhead, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int?> GetOrgCompIdAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SetStatusAsync(int id, string status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SetDisciplineAsync(int id, string discipline, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DiscoveryBackfillRow>> BackfillImportedOrgCompIdsAsync(bool apply, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> AddLanguagesAsync(int id, IEnumerable<string> languages, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetLastErrorAsync(int id, string? error, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetEmptySourceAsync(int id, bool empty, string by, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Превью по сценарию строки: JSON, дни, предупреждения. Помнит, кого тянули и что сняли из кэша.</summary>
    private sealed class FakePreviews : IDiscoveryPreviewService
    {
        public Dictionary<int, (string Json, int Days, string? Error)> Script { get; } = [];
        public List<int> Pulled { get; } = [];
        public HashSet<Guid> Live { get; } = [];

        public TimeSpan EntryLifetime => TimeSpan.FromMinutes(60);

        public Task<DiscoveryPreviewResult> PreviewAsync(int discoveredId, CancellationToken ct = default)
        {
            Pulled.Add(discoveredId);
            var (json, days, error) = Script.GetValueOrDefault(discoveredId, (FinishedJson, 1, null));
            if (error is not null) return Task.FromResult(DiscoveryPreviewResult.Failed(error));
            var parsed = new ParsedCompetition
            {
                Format = "IsrOrg",
                ResultsJson = json,
                ResultCount = 2,
                Competitions = [.. Enumerable.Range(0, days).Select(i => new ParsedCompetitionSummary("Meet", $"2{i}/11/2026", 2))],
            };
            var id = Guid.NewGuid();
            Live.Add(id);
            return Task.FromResult(new DiscoveryPreviewResult(id, parsed, ["he"], null, [], new ImportRecordPreviewDto { Count = 0 }, null));
        }

        public DiscoveryPreviewEntry? GetEntry(Guid previewId) => null;
        public void RemoveEntry(Guid previewId) => Live.Remove(previewId);
        public Task<DiscoveryProtocolPdf> FetchProtocolAsync(int discoveredId, string language, bool refreshIfMissing, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class Regulations(bool championship = false) : IRegulationFetchService
    {
        public Task<RegulationFetchDto> FetchAsync(int logligId, CancellationToken ct = default) =>
            Task.FromResult(new RegulationFetchDto(true, "https://loglig.com/LeagueTable/ShowLeagueDoc/1",
                new RegulationAnalysisDto(HasMedals: false, HasClubStanding: false, IsChampionship: championship, [])));
    }

    private sealed class Venues : IVenuePoolService
    {
        public Task<VenuePoolLookup> LookupAsync(string? venue, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<VenuePoolDirectory> LoadAsync(CancellationToken ct = default) => Task.FromResult(new VenuePoolDirectory(
        [
            new(VenueNameNormalizer.Normalize("בריכת אפק")!, new VenuePoolEntry(1, "בריכת אפק", "25m", false, null, "Vlad", Today)),
            new(VenueNameNormalizer.Normalize("מכון וינגייט")!, new VenuePoolEntry(2, "מכון וינגייט", null, true, null, "Vlad", Today)),
        ]));
    }

    private sealed record Harness(ResultsAutoService Service, SwimmDbContext Db, FakePreviews Previews);

    private static Harness Build(string? mode = "report", bool regulationChampionship = false)
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase("results-auto-" + Guid.NewGuid()).Options);
        var previews = new FakePreviews();
        var discovery = new DbDiscovery(db);

        var services = new ServiceCollection();
        services.AddSingleton<ICompetitionDiscoveryService>(discovery);
        services.AddSingleton<IDiscoveryPreviewService>(previews);
        services.AddSingleton<IRegulationFetchService>(new Regulations(regulationChampionship));
        var bulk = new BulkPullService(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BulkPullService>.Instance);

        var service = new ResultsAutoService(db, discovery, bulk, previews, new Venues(), new Settings(mode),
            NullLogger<ResultsAutoService>.Instance, new FixedTime(Now));
        return new Harness(service, db, previews);
    }

    private static DiscoveredCompetition Row(int id, string name = "ליגה מס 3 צעירים", string? venue = "בריכת אפק",
        DateTime? start = null, DateTime? end = null) => new()
    {
        Id = id, OrgCompId = 17000 + id, Name = name, DateStart = start ?? end ?? Today, DateEnd = end ?? Today,
        Venue = venue, LogligId = 16000 + id, Status = DiscoveredCompetitionStatus.New,
    };

    private static async Task<DiscoveredCompetition> Seed(Harness h, DiscoveredCompetition row)
    {
        h.Db.DiscoveredCompetitions.Add(row);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return row;
    }

    private static async Task<DiscoveredCompetition> Reload(Harness h, int id)
    {
        h.Db.ChangeTracker.Clear();
        return await h.Db.DiscoveredCompetitions.SingleAsync(d => d.Id == id);
    }

    private static async Task AssertNothingImported(Harness h)
    {
        Assert.Equal(0, await h.Db.Competitions.CountAsync());
        Assert.Equal(0, await h.Db.Results.CountAsync());
        Assert.Empty(h.Previews.Live); // разбор снят из кэша — импортировать его некому
    }

    [Fact]
    public async Task Green_row_all_gates_pass_would_take_and_nothing_imported()
    {
        var h = Build();
        await Seed(h, Row(1));

        var report = await h.Service.RunTickAsync();

        Assert.Equal(ResultsAutoVerdicts.WouldTake, report.Row?.Verdict);
        Assert.Equal("25m", report.Row?.PoolType);
        Assert.Equal(PoolTypeSources.Venue, report.Row?.PoolTypeSource);
        Assert.Equal("Clean", report.Row?.ClassifierVerdict);

        var row = await Reload(h, 1);
        Assert.Equal(ResultsAutoVerdicts.WouldTake, row.AutoPullVerdict);
        Assert.Null(row.AutoPullBlockedReason);
        Assert.Equal("25m", row.AutoPullPoolType);
        Assert.Equal(Now, row.AutoPullCheckedAt);
        Assert.Equal(DiscoveredCompetitionStatus.New, row.Status); // строка осталась «новой»
        await AssertNothingImported(h);
    }

    [Fact]
    public async Task Running_competition_not_touched_reason_named()
    {
        var h = Build();
        await Seed(h, Row(1));
        h.Previews.Script[1] = (RunningJson, 1, null);

        var report = await h.Service.RunTickAsync();

        var row = await Reload(h, 1);
        Assert.Equal(ResultsAutoVerdicts.NotFinished, row.AutoPullVerdict);
        Assert.Contains("не закончено", row.AutoPullBlockedReason);
        Assert.Contains("100 גב בנות", row.AutoPullBlockedReason);
        Assert.Null(row.AutoPullPoolType);
        Assert.Equal(1, report.Row?.PendingEvents);
        await AssertNothingImported(h);
    }

    [Fact]
    public async Task Championship_by_name_blocked_without_network()
    {
        var h = Build();
        await Seed(h, Row(1, name: "אליפות ישראל לנוער חורף 2026"));

        await h.Service.RunTickAsync();

        var row = await Reload(h, 1);
        Assert.Equal(ResultsAutoVerdicts.Blocked, row.AutoPullVerdict);
        Assert.Contains("чемпионат", row.AutoPullBlockedReason);
        Assert.Empty(h.Previews.Pulled); // PDF не качали
        await AssertNothingImported(h);
    }

    [Fact]
    public async Task Championship_by_regulation_blocked()
    {
        // Регламент чемпионата засчитывается, только если и в имени есть «אליפות» (без «ישראל»).
        var h = Build(regulationChampionship: true);
        await Seed(h, Row(1, name: "אליפות חורף ארנה גילאי 10-11"));

        await h.Service.RunTickAsync();

        var row = await Reload(h, 1);
        Assert.Equal(ResultsAutoVerdicts.Blocked, row.AutoPullVerdict);
        Assert.Contains("чемпионат", row.AutoPullBlockedReason);
        Assert.Single(h.Previews.Pulled);
    }

    [Fact]
    public async Task Multiday_by_dates_blocked()
    {
        var h = Build();
        await Seed(h, Row(1, start: Today.AddDays(-1), end: Today));

        await h.Service.RunTickAsync();

        var row = await Reload(h, 1);
        Assert.Equal(ResultsAutoVerdicts.Blocked, row.AutoPullVerdict);
        Assert.Contains("многодневка", row.AutoPullBlockedReason);
        Assert.Empty(h.Previews.Pulled);
    }

    [Fact]
    public async Task Multiday_file_blocked()
    {
        var h = Build();
        await Seed(h, Row(1));
        h.Previews.Script[1] = (FinishedJson, 2, null);

        await h.Service.RunTickAsync();

        var row = await Reload(h, 1);
        Assert.Equal(ResultsAutoVerdicts.Blocked, row.AutoPullVerdict);
        Assert.Contains("дней в файле: 2", row.AutoPullBlockedReason);
        await AssertNothingImported(h);
    }

    [Fact]
    public async Task Venue_not_in_directory_blocked()
    {
        var h = Build();
        await Seed(h, Row(1, venue: "בריכת מרום נווה רמת גן"));

        await h.Service.RunTickAsync();

        var row = await Reload(h, 1);
        Assert.Equal(ResultsAutoVerdicts.Blocked, row.AutoPullVerdict);
        Assert.Contains("нет в справочнике бассейнов", row.AutoPullBlockedReason);
        await AssertNothingImported(h);
    }

    [Fact]
    public async Task Ambiguous_venue_blocked()
    {
        var h = Build();
        await Seed(h, Row(1, venue: "מכון וינגייט"));

        await h.Service.RunTickAsync();

        var row = await Reload(h, 1);
        Assert.Equal(ResultsAutoVerdicts.Blocked, row.AutoPullVerdict);
        Assert.Contains("двусмысленное", row.AutoPullBlockedReason);
    }

    [Fact]
    public async Task Pull_failure_is_failed_verdict()
    {
        var h = Build();
        await Seed(h, Row(1));
        h.Previews.Script[1] = ("", 0, "Response status code does not indicate success: 503");

        await h.Service.RunTickAsync();

        var row = await Reload(h, 1);
        Assert.Equal(ResultsAutoVerdicts.Failed, row.AutoPullVerdict);
        Assert.Contains("503", row.AutoPullBlockedReason);
    }

    [Fact]
    public async Task Mode_off_does_nothing()
    {
        var h = Build(mode: null);
        await Seed(h, Row(1));

        var report = await h.Service.RunTickAsync();

        Assert.Equal(ResultsAutoModes.Off, report.Mode);
        Assert.Null(report.Row);
        Assert.Empty(h.Previews.Pulled);
        Assert.Null((await Reload(h, 1)).AutoPullCheckedAt);
    }

    [Fact]
    public async Task Mode_import_behaves_as_report_and_says_so()
    {
        var h = Build(mode: "import");
        await Seed(h, Row(1));

        var report = await h.Service.RunTickAsync();

        Assert.True(report.ImportNotImplemented);
        Assert.Equal(ResultsAutoVerdicts.WouldTake, report.Row?.Verdict);
        Assert.Contains("импорт ещё не реализован", JobRunSummaries.ResultsAuto(report));
        await AssertNothingImported(h);
    }

    [Fact]
    public async Task One_row_per_tick_and_recently_checked_skipped()
    {
        var h = Build();
        await Seed(h, Row(1));
        await Seed(h, Row(2, end: Today.AddDays(-1)));

        var first = await h.Service.RunTickAsync();
        var second = await h.Service.RunTickAsync();
        var third = await h.Service.RunTickAsync();

        Assert.Equal(1, first.Row?.DiscoveredId);  // свежий старт первым
        Assert.Equal(2, second.Row?.DiscoveredId);
        Assert.Null(third.Row);                    // оба проверены только что — шаг сутки
        Assert.Equal(2, third.InWindow);
        Assert.Equal([1, 2], h.Previews.Pulled);
    }

    [Fact]
    public async Task Rows_already_in_db_or_too_old_are_not_candidates()
    {
        var h = Build();
        await Seed(h, Row(1, end: Today.AddDays(-9)));
        await Seed(h, Row(2));
        h.Db.Competitions.Add(new Competition { Name = "ליגה", Date = "20/11/2026", OrgCompId = 17002 });
        await h.Db.SaveChangesAsync();

        var report = await h.Service.RunTickAsync();

        Assert.Null(report.Row);
        Assert.Equal(0, report.InWindow);
    }
}
