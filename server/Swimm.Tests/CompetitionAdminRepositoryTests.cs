using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

public class CompetitionAdminRepositoryTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static DbContextOptions<SwimmDbContext> BuildOptions(string name) =>
        new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static SwimmDbContext CreateDb(string name) =>
        new SwimmDbContext(BuildOptions(name));

    /// <summary>ICacheService, который всегда возвращает miss — репозиторий идёт в БД.</summary>
    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static ICacheService NoCache() => new NullCacheService();

    private static CompetitionInputDto ValidInput(string poolType) => new()
    {
        Name = "TestComp",
        Date = "01/01/2024",
        PoolType = poolType,
        Country = "ISR",
        CategoryKeys = []
    };

    // ── тесты ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_RejectsInvalidPoolType()
    {
        await using var db = CreateDb(nameof(Create_RejectsInvalidPoolType));
        var repo = new CompetitionAdminRepository(db, NoCache());

        var result = await repo.CreateAsync(ValidInput("50 m"));

        Assert.False(result.Success);
        Assert.Contains("бассейн", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Competitions);
    }

    [Fact]
    public async Task Create_RejectsEmptyPoolType()
    {
        await using var db = CreateDb(nameof(Create_RejectsEmptyPoolType));
        var repo = new CompetitionAdminRepository(db, NoCache());

        var result = await repo.CreateAsync(ValidInput(""));

        Assert.False(result.Success);
        Assert.Contains("бассейн", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Competitions);
    }

    [Fact]
    public async Task Create_AcceptsCanonicalPoolType()
    {
        await using var db = CreateDb(nameof(Create_AcceptsCanonicalPoolType));
        var repo = new CompetitionAdminRepository(db, NoCache());

        var result = await repo.CreateAsync(ValidInput("50m"));

        Assert.True(result.Success);
        Assert.Single(db.Competitions);
    }

    // ── GetUnifiedAsync (объединённый список Competitions + Discovery) ──────────

    [Fact]
    public async Task GetUnified_AssignsStagesAcrossSources()
    {
        await using var db = CreateDb(nameof(GetUnified_AssignsStagesAcrossSources));
        // Imported: соревнование со штампом OrgCompId + discovery-строка с тем же compID.
        db.Competitions.Add(new Competition { Id = 1, Name = "Imported comp", Date = "05/07/2026", PoolType = "50m", OrgCompId = 100 });
        // DbOnly: соревнование без OrgCompId, ни одна discovery-строка на него не матчится.
        db.Competitions.Add(new Competition { Id = 2, Name = "PDF only comp", Date = "01/01/2020", PoolType = "25m" });
        // Синтетика: по умолчанию должна быть скрыта.
        db.Competitions.Add(new Competition { Id = 3, Name = "SYNTH Meet 0001", Date = "07/01/2016", PoolType = "50m" });
        db.DiscoveredCompetitions.AddRange(
            new DiscoveredCompetition
            {
                Id = 10, OrgCompId = 100, Name = "Imported comp",
                DateStart = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc), Status = "imported"
            },
            new DiscoveredCompetition // OnSite: на сайте есть, в БД нет
            {
                Id = 11, OrgCompId = 200, Name = "Future site comp",
                DateStart = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), Status = "new"
            },
            new DiscoveredCompetition // Ignored
            {
                Id = 12, OrgCompId = 300, Name = "Hidden site comp",
                DateStart = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Status = "ignored"
            });
        await db.SaveChangesAsync();

        var repo = new CompetitionAdminRepository(db, NoCache());
        var all = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: null, 1, 20);

        // SYNTH скрыта по умолчанию → 4 (imported/dbOnly/onSite/ignored), без синтетики.
        Assert.Equal(4, all.Page.TotalCount);
        var byStage = all.Page.Items.GroupBy(u => u.Stage).ToDictionary(g => g.Key, g => g.ToList());
        Assert.Single(byStage[CompetitionStage.Imported]);
        Assert.Single(byStage[CompetitionStage.DbOnly]);
        Assert.Single(byStage[CompetitionStage.OnSite]);
        Assert.Single(byStage[CompetitionStage.Ignored]);

        // Imported-строка несёт обе стороны; site-оверлей — та самая discovery-строка.
        var imported = byStage[CompetitionStage.Imported][0];
        Assert.NotNull(imported.Db);
        Assert.Equal(100, imported.Site!.OrgCompId);

        // Счётчики по месяцам: imported (05.07), onSite (01.08), ignored (01.03), dbOnly (01.01).
        Assert.Equal(1, all.MonthCounts[6]);  // июль
        Assert.Equal(1, all.MonthCounts[7]);  // август
        Assert.Equal(1, all.MonthCounts[2]);  // март
        Assert.Equal(1, all.MonthCounts[0]);  // январь

        // Фильтр по месяцу: только июльская (imported) строка.
        var july = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: 7, 1, 20);
        Assert.Equal(1, july.Page.TotalCount);
        Assert.Equal(CompetitionStage.Imported, july.Page.Items[0].Stage);

        // Фильтр по стадии.
        var onSiteOnly = await repo.GetUnifiedAsync(null, null, null, "OnSite", showSynthetic: false, month: null, 1, 20);
        Assert.Equal(1, onSiteOnly.Page.TotalCount);
        Assert.Equal(200, onSiteOnly.Page.Items[0].Site!.OrgCompId);

        // Показ синтетики — SYNTH-строка появляется (DbOnly).
        var withSynth = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: true, month: null, 1, 20);
        Assert.Equal(5, withSynth.Page.TotalCount);
        Assert.Contains(withSynth.Page.Items, u => u.Db?.Single?.Name == "SYNTH Meet 0001");

        // Фильтр по сезону (сен–авг, по году окончания): 2026 = imported/onSite/ignored,
        // «PDF only comp» (01.01.2020) — сезон 2020. Работает и по строкам с сайта.
        var season2026 = await repo.GetUnifiedAsync(null, null, 2026, null, showSynthetic: false, month: null, 1, 20);
        Assert.Equal(3, season2026.Page.TotalCount);
        var season2020 = await repo.GetUnifiedAsync(null, null, 2020, null, showSynthetic: false, month: null, 1, 20);
        Assert.Equal(1, season2020.Page.TotalCount);
        Assert.Equal("PDF only comp", season2020.Page.Items[0].Db!.Single!.Name);

        // Чипы-сезоны: считаются ДО фильтра по сезону (иначе чип остался бы один) и несут
        // «затянуто из всего» — затянуто = строка есть в БД (Imported/DbOnly).
        Assert.Equal([2026, 2020], all.SeasonCounts!.Select(s => s.Season));
        var chip2026 = all.SeasonCounts!.First(s => s.Season == 2026);
        Assert.Equal(3, chip2026.Total);     // imported + onSite + ignored
        Assert.Equal(1, chip2026.Imported);  // в БД только imported
        Assert.Equal(new SeasonCountDto(2020, 1, 1), all.SeasonCounts!.First(s => s.Season == 2020));
        // Сезон выбран → чипы прежние, а счётчики месяцев уже по сезону.
        Assert.Equal(2, season2026.SeasonCounts!.Count);
        Assert.Equal(0, season2026.MonthCounts[0]);   // январь 2020 в сезон 2026 не входит
        Assert.Equal(1, season2026.MonthImported![6]); // июль: затянута одна из одной

        // Список сезонов для селекта — из обеих сторон, по убыванию.
        var seasons = await repo.GetAvailableSeasonsAsync();
        Assert.Equal(seasons.OrderByDescending(s => s), seasons);
        Assert.Contains(2026, seasons);
        Assert.Contains(2020, seasons);

        // T3b qualityFilter: no-org-comp-id — только «PDF only comp» (без OrgCompId).
        var noOrgCompId = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: null, 1, 20, qualityFilter: "no-org-comp-id");
        Assert.Equal(1, noOrgCompId.Page.TotalCount);
        Assert.Equal("PDF only comp", noOrgCompId.Page.Items[0].Db!.Single!.Name);

        // T3b qualityFilter: no-results — оба соревнования без результатов.
        var noResults = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: null, 1, 20, qualityFilter: "no-results");
        Assert.Equal(2, noResults.Page.TotalCount);

        // T3b qualityFilter: discovery-error — ни одна discovery-строка не помечена ошибкой.
        var discoveryError = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: null, 1, 20, qualityFilter: "discovery-error");
        Assert.Equal(0, discoveryError.Page.TotalCount);
    }

    [Fact]
    public async Task GetUnified_QualityFilter_DiscoveryError_MatchesLastError()
    {
        await using var db = CreateDb(nameof(GetUnified_QualityFilter_DiscoveryError_MatchesLastError));
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            Id = 1, OrgCompId = 500, Name = "Errored comp",
            DateStart = DateTime.UtcNow, DateEnd = DateTime.UtcNow, Status = "new", LastError = "timeout"
        });
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            Id = 2, OrgCompId = 501, Name = "Ok comp",
            DateStart = DateTime.UtcNow, DateEnd = DateTime.UtcNow, Status = "new"
        });
        await db.SaveChangesAsync();

        var repo = new CompetitionAdminRepository(db, NoCache());
        var result = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: null, 1, 20, qualityFilter: "discovery-error");

        Assert.Equal(1, result.Page.TotalCount);
        Assert.Equal("Errored comp", result.Page.Items[0].Site!.Name);
    }

    [Fact]
    public async Task GetUnified_KindChamp_KeepsChampionshipsFromBothSides()
    {
        await using var db = CreateDb(nameof(GetUnified_KindChamp_KeepsChampionshipsFromBothSides));
        // У соревнований БД решает ФЛАГ, а не название.
        db.Competitions.Add(new Competition { Id = 1, Name = "אליפות ישראל קיץ 2026", Date = "05/07/2026", PoolType = "50m", IsChampionship = true });
        db.Competitions.Add(new Competition { Id = 2, Name = "Israel Championship 2026", Date = "06/07/2026", PoolType = "50m", IsChampionship = true });
        db.Competitions.Add(new Competition { Id = 3, Name = "ליגה מספר 6", Date = "07/07/2026", PoolType = "50m" });
        // Название «чемпионское», но галка снята руками — в фильтр попадать НЕ должно.
        db.Competitions.Add(new Competition { Id = 4, Name = "אליפות ישראל ישנה", Date = "10/07/2026", PoolType = "50m", IsChampionship = false });
        db.DiscoveredCompetitions.AddRange(
            new DiscoveredCompetition
            {
                // Спонсор между словами — подстрокой «אליפות ישראל» такое не поймать.
                Id = 10, OrgCompId = 900, Name = "מוקדמות אליפות \"ארנה\" ישראל קיץ 2026",
                DateStart = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc), Status = "new"
            },
            new DiscoveredCompetition
            {
                Id = 11, OrgCompId = 901, Name = "ליגה מס 4",
                DateStart = new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc), Status = "new"
            });
        await db.SaveChangesAsync();

        var repo = new CompetitionAdminRepository(db, NoCache());
        var all = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: null, 1, 20);
        Assert.Equal(6, all.Page.TotalCount);

        // Чемпионат Израиля: два помеченных флагом из БД + строка «только на сайте», где флага
        // взять негде и работает эвристика по названию. Лига и снятая галка отсеяны.
        var champs = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: null, 1, 20, kind: "champ");
        Assert.Equal(3, champs.Page.TotalCount);
        var champNames = champs.Page.Items.Select(u => u.Db?.Single?.Name ?? u.Site!.Name).ToList();
        Assert.DoesNotContain(champNames, n => n.StartsWith("ליגה"));
        Assert.DoesNotContain(champNames, n => n == "אליפות ישראל ישנה");
        // Счётчики месяцев считаются уже под фильтром.
        Assert.Equal(3, champs.MonthCounts[6]);
    }

    [Fact]
    public async Task QuickUpdate_AppliesToAllDaysOfEvent()
    {
        await using var db = CreateDb(nameof(QuickUpdate_AppliesToAllDaysOfEvent));
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 5, Name = "Событие" });
        db.Competitions.AddRange(
            new Competition { Id = 1, Name = "День 1", Date = "01/07/2026", PoolType = "25m", EventId = 5, DayNumber = 1 },
            new Competition { Id = 2, Name = "День 2", Date = "02/07/2026", PoolType = "25m", EventId = 5, DayNumber = 2 },
            // Чужое соревнование — не должно зацепить.
            new Competition { Id = 3, Name = "Другое", Date = "03/07/2026", PoolType = "25m" });
        await db.SaveChangesAsync();

        var repo = new CompetitionAdminRepository(db, NoCache());
        var result = await repo.QuickUpdateAsync(new CompetitionQuickEditDto
        {
            CompetitionId = 2, // открыли панель у второго дня — применяется всё равно ко всем
            PoolType = "50m",
            IsAward = true,
            IsChampionship = true,
            ShowCombineAllResults = true
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.Id); // Id = число изменённых дней
        var days = await db.Competitions.Where(c => c.EventId == 5).ToListAsync();
        Assert.All(days, d =>
        {
            Assert.Equal("50m", d.PoolType);
            Assert.True(d.IsAward);
            Assert.True(d.IsChampionship);
            Assert.True(d.ShowCombineAllResults);
        });
        var other = await db.Competitions.SingleAsync(c => c.Id == 3);
        Assert.Equal("25m", other.PoolType);
        Assert.False(other.IsChampionship);
    }

    /// <summary>Граница сезона — сентябрь: он уже относится к следующему (как cYear на isr.org.il).</summary>
    [Theory]
    [InlineData("2024-08-31", 2024)]
    [InlineData("2024-09-01", 2025)]
    [InlineData("2024-10-10", 2025)]
    [InlineData("2025-08-31", 2025)]
    public void SeasonOf_SeptemberStartsNextSeason(string iso, int expected) =>
        Assert.Equal(expected, CompetitionAdminRepository.SeasonOf(DateTime.Parse(iso)));
}
