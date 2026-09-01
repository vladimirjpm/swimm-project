using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>Синхронизация «входящих» автозабора + матчинг «уже импортировано» (фаза 6).</summary>
public class CompetitionDiscoveryServiceTests
{
    private sealed class FakeProvider : ICompetitionDiscoveryProvider
    {
        public List<DiscoveredListItem> Finished { get; } = [];
        public List<DiscoveredListItem> Upcoming { get; } = [];

        /// <summary>Сезон последнего запроса — проверяем, что cYear доезжает до провайдера.</summary>
        public int? LastYear { get; private set; }

        public Task<IReadOnlyList<DiscoveredListItem>> FetchListAsync(
            bool finished, int? year = null, CancellationToken ct = default)
        {
            LastYear = year;
            return Task.FromResult<IReadOnlyList<DiscoveredListItem>>(finished ? Finished : Upcoming);
        }

        /// <summary>OrgCompId, для которого FetchDetailsAsync должен упасть (тест «одна строка не роняет прогон»).</summary>
        public HashSet<int> FailFor { get; } = [];

        public Task<DiscoveredDetails> FetchDetailsAsync(int orgCompId, CancellationToken ct = default)
            => FailFor.Contains(orgCompId)
                ? throw new HttpRequestException("сеть недоступна")
                : Task.FromResult(new DiscoveredDetails("N", "V", 123, 1));

        public Task<byte[]> FetchResultsPdfAsync(int logligId, string culture = "he-IL", CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<int>> FetchEventIdsAsync(int logligId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<LogligEventResultsDto> FetchEventResultsAsync(int eventId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LogligDisciplineGridRowDto>> FetchDisciplineGridAsync(
            int logligId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<LogligStartListDto> FetchStartListAsync(int disciplineId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static CompetitionDiscoveryService CreateService(SwimmDbContext db, FakeProvider provider) =>
        new(db, provider, NullLogger<CompetitionDiscoveryService>.Instance);

    private static DiscoveredListItem Item(int id, string name, string startIso, string? endIso = null) =>
        new(id, name,
            DateTime.SpecifyKind(DateTime.Parse(startIso), DateTimeKind.Utc),
            DateTime.SpecifyKind(DateTime.Parse(endIso ?? startIso), DateTimeKind.Utc));

    [Fact]
    public async Task Sync_AddsNew_UpdatesChanged_KeepsStatus()
    {
        await using var db = CreateDb(nameof(Sync_AddsNew_UpdatesChanged_KeepsStatus));
        var provider = new FakeProvider();
        provider.Finished.Add(Item(100, "Старое имя", "2026-06-01"));
        var svc = CreateService(db, provider);

        var first = await svc.SyncAsync();
        Assert.Equal(1, first.Added);

        // Пометим ignored и «переименуем» на сайте — статус должен сохраниться, имя обновиться.
        var row = await db.DiscoveredCompetitions.SingleAsync();
        row.Status = DiscoveredCompetitionStatus.Ignored;
        await db.SaveChangesAsync();

        provider.Finished[0] = Item(100, "Новое имя", "2026-06-01");
        provider.Upcoming.Add(Item(200, "Будущее", "2026-09-01"));
        var second = await svc.SyncAsync();

        Assert.Equal(1, second.Added);
        Assert.Equal(1, second.Updated);
        var updated = await db.DiscoveredCompetitions.SingleAsync(d => d.OrgCompId == 100);
        Assert.Equal("Новое имя", updated.Name);
        Assert.Equal(DiscoveredCompetitionStatus.Ignored, updated.Status);
    }

    [Fact]
    public async Task Sync_PassesSeasonYearToProvider()
    {
        await using var db = CreateDb(nameof(Sync_PassesSeasonYearToProvider));
        var provider = new FakeProvider();
        provider.Finished.Add(Item(300, "Сезон 24/25", "2025-03-01"));
        var svc = CreateService(db, provider);

        await svc.SyncAsync();
        Assert.Null(provider.LastYear); // без аргумента — текущий сезон сайта

        var past = await svc.SyncAsync(2025);
        Assert.Equal(2025, provider.LastYear);
        Assert.Equal(1, past.TotalOnSite);
    }

    [Fact]
    public async Task GetAll_MatchesImportedByNameAndDate()
    {
        await using var db = CreateDb(nameof(GetAll_MatchesImportedByNameAndDate));
        db.Competitions.Add(new Competition { Name = "ליגה מס 4", Date = "03/07/2026", PoolType = "25m" });
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            OrgCompId = 1, Name = " ליגה  מס 4 ", // лишние пробелы — нормализация должна съесть
            DateStart = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            DateEnd = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc)
        });
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            OrgCompId = 2, Name = "ליגה מס 4", // то же имя, другая дата — НЕ матч
            DateStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateEnd = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var all = await CreateService(db, new FakeProvider()).GetAllAsync();

        Assert.Equal("ליגה מס 4", all.Single(d => d.OrgCompId == 1).MatchedCompetitionName);
        Assert.Null(all.Single(d => d.OrgCompId == 2).MatchedCompetitionName);
    }

    [Fact]
    public async Task GetAll_MatchesDistrictSuffixAndMangledQuotes()
    {
        // Реальный кейс Arena 8-11 חורף 2026: сайт дописывает суффикс района, а в БД
        // кавычки вокруг «ארנה» то есть, то нет, то с литеральными бэкслешами.
        await using var db = CreateDb(nameof(GetAll_MatchesDistrictSuffixAndMangledQuotes));
        db.Competitions.Add(new Competition
        {
            Name = "אליפות ישראל \\\"ארנה\\\" לגילאי 8-11 חורף 2026", // литеральные \" из импорта
            Date = "15/02/2026", PoolType = "25m"
        });
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            OrgCompId = 1, Name = "אליפות ישראל \"ארנה\" לגילאי 8-11 חורף 2026- מחוז צפון",
            DateStart = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc),
            DateEnd = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var all = await CreateService(db, new FakeProvider()).GetAllAsync();

        Assert.NotNull(all.Single(d => d.OrgCompId == 1).MatchedCompetitionName);
    }

    [Fact]
    public async Task AddLanguagesAsync_He_PersistsMonolingualVerdict()
    {
        // Регрессия: DiscoveryAdminController.SyncLanguages, ветка «monolingual» (обе культуры
        // отдают один и тот же PDF — второй языковой версии на loglig нет), должна пометить
        // запись Languages="he", чтобы кнопка «Синхр. языки» перестала показываться и рендер
        // строки объяснял, почему. Раньше эта ветка возвращала 200, но ничего не сохраняла.
        await using var db = CreateDb(nameof(AddLanguagesAsync_He_PersistsMonolingualVerdict));
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            Id = 100, OrgCompId = 16825, Name = "אליפות חדרה הפתוחה 2026",
            DateStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateEnd = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = DiscoveredCompetitionStatus.Imported
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new FakeProvider());
        var ok = await svc.AddLanguagesAsync(100, ["he"]);

        Assert.True(ok);
        var row = await db.DiscoveredCompetitions.SingleAsync(d => d.Id == 100);
        Assert.Equal("he", row.Languages);
    }

    [Fact]
    public async Task GetAll_ReturnsMatchedCompetitionId()
    {
        await using var db = CreateDb(nameof(GetAll_ReturnsMatchedCompetitionId));
        var comp = new Competition { Name = "ליגה מס 4", Date = "03/07/2026", PoolType = "25m" };
        db.Competitions.Add(comp);
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            OrgCompId = 1, Name = "ליגה מס 4",
            DateStart = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            DateEnd = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var dto = (await CreateService(db, new FakeProvider()).GetAllAsync()).Single(d => d.OrgCompId == 1);
        Assert.Equal(comp.Id, dto.MatchedCompetitionId);
    }

    [Fact]
    public async Task GetAll_FallsBackToOrgCompIdLink_WhenNameDateMatcherMisses()
    {
        // Кросс-языковой случай: Discovery «מכביה» (иврит), справочник «Maccabiah 2026» (англ.) —
        // матчер по имени+дате НЕ спарит, но OrgCompId уже штампован (ручная привязка). Ожидаем,
        // что fallback по OrgCompId всё равно вернёт линк.
        await using var db = CreateDb(nameof(GetAll_FallsBackToOrgCompIdLink_WhenNameDateMatcherMisses));
        var comp = new Competition { Name = "Maccabiah 2026", Date = "05/07/2026", PoolType = "50m", OrgCompId = 16723 };
        db.Competitions.Add(comp);
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            OrgCompId = 16723, Name = "מכביה",
            DateStart = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
            DateEnd = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var dto = (await CreateService(db, new FakeProvider()).GetAllAsync()).Single(d => d.OrgCompId == 16723);
        Assert.Equal(comp.Id, dto.MatchedCompetitionId);
        Assert.Equal("Maccabiah 2026", dto.MatchedCompetitionName);
    }

    // ── Батч-бэкфилл (CLI --backfill-discovery-orgcompid) ───────────────────────

    [Fact]
    public async Task BackfillImportedOrgCompIds_DryRun_ReportsWouldLink_DoesNotWrite()
    {
        await using var db = CreateDb(nameof(BackfillImportedOrgCompIds_DryRun_ReportsWouldLink_DoesNotWrite));
        var matched = new Competition { Name = "ליגה מס 4", Date = "03/07/2026", PoolType = "25m" };
        var takenComp = new Competition { Name = "Другое соревнование", Date = "01/01/2026", PoolType = "25m", OrgCompId = 999 };
        db.Competitions.AddRange(matched, takenComp);
        db.DiscoveredCompetitions.AddRange(
            new DiscoveredCompetition
            {
                Id = 1, OrgCompId = 777, Name = "ליגה מס 4",
                DateStart = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
                Status = DiscoveredCompetitionStatus.Imported
            },
            new DiscoveredCompetition
            {
                Id = 2, OrgCompId = 888, Name = "Соревнование без импорта",
                DateStart = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = DiscoveredCompetitionStatus.New
            },
            new DiscoveredCompetition
            {
                Id = 3, OrgCompId = 999, Name = "Другое соревнование",
                DateStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = DiscoveredCompetitionStatus.Imported
            });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new FakeProvider());
        var report = await svc.BackfillImportedOrgCompIdsAsync(apply: false);

        // Строка без матча (Id=2) не попадает в отчёт.
        Assert.Equal(2, report.Count);
        Assert.Equal("WouldLink", report.Single(r => r.OrgCompId == 777).Action);
        Assert.Equal("AlreadyLinked", report.Single(r => r.OrgCompId == 999).Action);

        // dry-run — БД не изменена.
        Assert.Null((await db.Competitions.SingleAsync(c => c.Id == matched.Id)).OrgCompId);
    }

    [Fact]
    public async Task BackfillImportedOrgCompIds_Apply_StampsOrgCompId_ThenIdempotent()
    {
        await using var db = CreateDb(nameof(BackfillImportedOrgCompIds_Apply_StampsOrgCompId_ThenIdempotent));
        var matched = new Competition { Name = "ליגה מס 4", Date = "03/07/2026", PoolType = "25m" };
        db.Competitions.Add(matched);
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            Id = 1, OrgCompId = 777, Name = "ליגה מס 4",
            DateStart = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            DateEnd = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            Status = DiscoveredCompetitionStatus.Imported
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new FakeProvider());
        var report = await svc.BackfillImportedOrgCompIdsAsync(apply: true);

        Assert.Equal("Linked", report.Single().Action);
        Assert.Equal(777, (await db.Competitions.SingleAsync(c => c.Id == matched.Id)).OrgCompId);

        // Повторный вызов — идемпотентно.
        var again = await svc.BackfillImportedOrgCompIdsAsync(apply: true);
        Assert.Equal("AlreadyLinked", again.Single().Action);
    }

    // ── С2: догрузка деталей будущих стартов (docs/tasks/start-list-ops-sonnet.md) ──────

    [Fact]
    public async Task RefreshUpcomingDetailsAsync_OnlyFutureRowsWithoutLogligId_WithinWindow()
    {
        await using var db = CreateDb(nameof(RefreshUpcomingDetailsAsync_OnlyFutureRowsWithoutLogligId_WithinWindow));
        var today = DateTime.UtcNow.Date;
        db.DiscoveredCompetitions.AddRange(
            new DiscoveredCompetition // будущий, без loglig-id, в окне — берём
            {
                Id = 1, OrgCompId = 1, Name = "В окне", DateStart = today.AddDays(5), DateEnd = today.AddDays(5),
                Status = DiscoveredCompetitionStatus.New
            },
            new DiscoveredCompetition // будущий, без loglig-id, ЗА окном — не берём
            {
                Id = 2, OrgCompId = 2, Name = "За окном", DateStart = today.AddDays(30), DateEnd = today.AddDays(30),
                Status = DiscoveredCompetitionStatus.New
            },
            new DiscoveredCompetition // прошедший, без loglig-id — не берём (решение 3)
            {
                Id = 3, OrgCompId = 3, Name = "Прошедший", DateStart = today.AddDays(-5), DateEnd = today.AddDays(-5),
                Status = DiscoveredCompetitionStatus.New
            },
            new DiscoveredCompetition // будущий, уже с loglig-id — не берём
            {
                Id = 4, OrgCompId = 4, Name = "Уже есть", DateStart = today.AddDays(3), DateEnd = today.AddDays(3),
                Status = DiscoveredCompetitionStatus.New, LogligId = 999
            },
            new DiscoveredCompetition // будущий, но ignored — не берём
            {
                Id = 5, OrgCompId = 5, Name = "Скрыт", DateStart = today.AddDays(2), DateEnd = today.AddDays(2),
                Status = DiscoveredCompetitionStatus.Ignored
            });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new FakeProvider());
        var (checkedCount, resolved) = await svc.RefreshUpcomingDetailsAsync(daysAhead: 14);

        Assert.Equal(1, checkedCount);
        Assert.Equal(1, resolved);
        Assert.Equal(123, (await db.DiscoveredCompetitions.SingleAsync(d => d.Id == 1)).LogligId);
        // Остальные не тронуты.
        Assert.Null((await db.DiscoveredCompetitions.SingleAsync(d => d.Id == 2)).LogligId);
        Assert.Null((await db.DiscoveredCompetitions.SingleAsync(d => d.Id == 3)).LogligId);
        Assert.Equal(999, (await db.DiscoveredCompetitions.SingleAsync(d => d.Id == 4)).LogligId);
    }

    [Fact]
    public async Task RefreshUpcomingDetailsAsync_OneRowFails_RestStillProcessed()
    {
        await using var db = CreateDb(nameof(RefreshUpcomingDetailsAsync_OneRowFails_RestStillProcessed));
        var today = DateTime.UtcNow.Date;
        db.DiscoveredCompetitions.AddRange(
            new DiscoveredCompetition
            {
                Id = 1, OrgCompId = 1, Name = "Падает", DateStart = today.AddDays(1), DateEnd = today.AddDays(1),
                Status = DiscoveredCompetitionStatus.New
            },
            new DiscoveredCompetition
            {
                Id = 2, OrgCompId = 2, Name = "Ок", DateStart = today.AddDays(2), DateEnd = today.AddDays(2),
                Status = DiscoveredCompetitionStatus.New
            });
        await db.SaveChangesAsync();

        var provider = new FakeProvider();
        provider.FailFor.Add(1);
        var svc = CreateService(db, provider);

        var (checkedCount, resolved) = await svc.RefreshUpcomingDetailsAsync(daysAhead: 14);

        Assert.Equal(2, checkedCount);
        Assert.Equal(1, resolved); // строка 2 добыта, несмотря на сбой строки 1
        Assert.Null((await db.DiscoveredCompetitions.SingleAsync(d => d.Id == 1)).LogligId);
        Assert.NotNull((await db.DiscoveredCompetitions.SingleAsync(d => d.Id == 1)).LastError);
        Assert.Equal(123, (await db.DiscoveredCompetitions.SingleAsync(d => d.Id == 2)).LogligId);
    }

    [Fact]
    public async Task GetOrgCompIdAsync_ReturnsOrgCompId_OrNullWhenMissing()
    {
        await using var db = CreateDb(nameof(GetOrgCompIdAsync_ReturnsOrgCompId_OrNullWhenMissing));
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            Id = 10, OrgCompId = 16786, Name = "X",
            DateStart = DateTime.UtcNow, DateEnd = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new FakeProvider());
        Assert.Equal(16786, await svc.GetOrgCompIdAsync(10));
        Assert.Null(await svc.GetOrgCompIdAsync(999));
    }
}
