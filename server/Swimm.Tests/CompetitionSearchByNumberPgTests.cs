using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Поиск /Admin/Competitions по НОМЕРУ: compID федерации или Id соревнования в базе (решение
/// Влада 22.11.2026). Находки /Admin/Health ведут на список по `?q=&lt;Id соревнования&gt;`, а поиск
/// понимал только имя — ссылка открывала пустой список.
///
/// На НАСТОЯЩЕМ Postgres: имя ищется через ILIKE, которого нет у InMemory. Тест только читает
/// локальную базу (берёт из неё живые строки); нет базы или нужных строк — пропуск.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class CompetitionSearchByNumberPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    private static Task<Swimm.Application.Dtos.UnifiedCompetitionList> Search(SwimmDbContext db, string q) =>
        new CompetitionAdminRepository(db).GetUnifiedAsync(
            q, null, null, null, showSynthetic: true, month: null, 1, 50, discipline: "all");

    [Fact]
    public async Task SingleCompetition_FoundByItsIdAndByCompId()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var c = await db.Competitions.AsNoTracking()
            .Where(x => x.EventId == null && x.OrgCompId != null)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (c == null) return;

        var byId = await Search(db, c.Id.ToString());
        Assert.Contains(byId.Page.Items, u => u.Db?.Single?.Id == c.Id);

        var byCompId = await Search(db, $" {c.OrgCompId} ");
        Assert.Contains(byCompId.Page.Items, u => u.Db?.Single?.Id == c.Id);
    }

    [Fact]
    public async Task EventDay_FoundByIdOfAnyDay_NotOnlyTheHead()
    {
        await using var db = TryCreate();
        if (db == null) return;

        // День, который НЕ голова события: голова — день с минимальным DayNumber.
        var day = await db.Competitions.AsNoTracking()
            .Where(x => x.EventId != null && x.DayNumber > db.Competitions
                .Where(y => y.EventId == x.EventId).Min(y => y.DayNumber))
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (day == null) return;

        var list = await Search(db, day.Id.ToString());
        Assert.Contains(list.Page.Items, u => u.Db?.EventId == day.EventId);
    }

    [Fact]
    public async Task SiteOnlyRow_FoundByCompId()
    {
        await using var db = TryCreate();
        if (db == null) return;

        // Строка «Входящих», которой в базе нет (по номеру федерации).
        var d = await db.DiscoveredCompetitions.AsNoTracking()
            .Where(x => x.Status == "new" && !db.Competitions.Any(c => c.OrgCompId == x.OrgCompId))
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (d == null) return;

        var list = await Search(db, d.OrgCompId.ToString());
        Assert.Contains(list.Page.Items, u => u.Db == null && u.Site?.DiscoveredId == d.Id);
    }

    [Fact]
    public async Task NumberStillMatchesNames_YearInTitle()
    {
        await using var db = TryCreate();
        if (db == null) return;

        // «2026» — и номер, и год в названии: имена с годом обязаны находиться по-прежнему.
        var withYear = await db.Competitions.AsNoTracking()
            .Where(x => x.EventId == null && x.Name.Contains("2026")).OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (withYear == null) return;

        var list = await Search(db, "2026");
        Assert.True(list.Page.TotalCount > 0);
        Assert.All(list.Page.Items, u => Assert.True(
            (u.Db?.Single?.Name ?? u.Db?.EventName ?? u.Site?.Name ?? "").Contains("2026")
            || u.Db?.Single?.Id == 2026 || u.Db?.Single?.OrgCompId == 2026 || u.Site?.OrgCompId == 2026
            || u.Db?.Days.Any(dd => dd.Id == 2026 || dd.OrgCompId == 2026) == true
            || (u.Db?.Single?.SubName ?? "").Contains("2026")));
    }
}
