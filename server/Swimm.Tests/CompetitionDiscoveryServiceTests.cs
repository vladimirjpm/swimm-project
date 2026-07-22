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

        public Task<IReadOnlyList<DiscoveredListItem>> FetchListAsync(bool finished, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DiscoveredListItem>>(finished ? Finished : Upcoming);

        public Task<DiscoveredDetails> FetchDetailsAsync(int orgCompId, CancellationToken ct = default)
            => Task.FromResult(new DiscoveredDetails("N", "V", 123, 1));

        public Task<byte[]> FetchResultsPdfAsync(int logligId, string culture = "he-IL", CancellationToken ct = default)
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

    // ── Бэкфилл связи Discovery → Competition (проставление OrgCompId) ─────────

    [Fact]
    public async Task LinkImported_StampsOrgCompId_OnMatchedCompetition()
    {
        await using var db = CreateDb(nameof(LinkImported_StampsOrgCompId_OnMatchedCompetition));
        var comp = new Competition { Name = "ליגה מס 4", Date = "03/07/2026", PoolType = "25m" };
        db.Competitions.Add(comp);
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            Id = 5, OrgCompId = 777, Name = "ליגה מס 4",
            DateStart = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            DateEnd = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            Status = DiscoveredCompetitionStatus.Imported
        });
        await db.SaveChangesAsync();

        var res = await CreateService(db, new FakeProvider()).LinkImportedAsync(5);

        Assert.True(res.Ok);
        Assert.False(res.AlreadyLinked);
        Assert.Equal(777, (await db.Competitions.SingleAsync(c => c.Id == comp.Id)).OrgCompId);

        // Повторный вызов — идемпотентно (already linked), OrgCompId не меняется.
        var again = await CreateService(db, new FakeProvider()).LinkImportedAsync(5);
        Assert.True(again.Ok);
        Assert.True(again.AlreadyLinked);
    }

    [Fact]
    public async Task LinkImported_NoMatch_ReturnsNotOk()
    {
        await using var db = CreateDb(nameof(LinkImported_NoMatch_ReturnsNotOk));
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            Id = 6, OrgCompId = 888, Name = "Соревнование без импорта",
            DateStart = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            DateEnd = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            Status = DiscoveredCompetitionStatus.Imported
        });
        await db.SaveChangesAsync();

        var res = await CreateService(db, new FakeProvider()).LinkImportedAsync(6);

        Assert.False(res.Ok);
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
}
