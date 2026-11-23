using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р118 (docs/data-integrity.md): превью и импорт сопоставляют день файла с существующим
/// соревнованием ОДНИМ правилом — <c>ImportCompetitionMatcher.MatchDay</c>: сперва день того же
/// compID по дате (Д2), потом (Name|SubName)|Date|PoolType.
///
/// Тупик, который это закрывает (c1973, compID 16926): протокол не пишет бассейн, парсер ставит
/// умолчание 25m, а в базе 50m. Превью искало только по ключу с бассейном — «не нашло»,
/// «Перезатянуть» не взводило перезапись, а импорт находил день по compID и откатывался «Дублем».
/// </summary>
public class RepullMatchingTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static object Item(string competition, string date, string lastName, int lane, string time = "00:30.00") => new
    {
        country = "ISR",
        competition,
        date,
        event_style_name = "Freestyle",
        event_style_len = "50",
        event_style_gender = "male",
        pool_type = "25m",
        position = lane,
        heat = 1,
        lane,
        last_name = lastName,
        first_name = "Tal",
        birth_year = 2012,
        club = "Club",
        time
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    private static ParsedCompetitionSummary Day(string name, string date, string? pool = "25m") =>
        new(name, date, 1, pool);

    /// <summary>Одиночное затянутое соревнование со штампом compID — как c1973.</summary>
    private static async Task<int> SeedSingleAsync(SwimmDbContext db, int orgCompId, string name = "Лига юг тур 2", string pool = "25m")
    {
        var c = new Competition { Name = name, Date = "21/11/2026", PoolType = pool, OrgCompId = orgCompId };
        db.Competitions.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    [Fact]
    public async Task Preview_FindsExisting_ByOrgCompId_WhenNameDiffers()
    {
        await using var db = CreateDb(nameof(Preview_FindsExisting_ByOrgCompId_WhenNameDiffers));
        var id = await SeedSingleAsync(db, 16926);

        var matches = await new JsonImportService(db, new NullCache())
            .FindExistingCompetitionsAsync([Day("Лига юг — тур 2 (исправлено)", "21/11/2026")], orgCompId: 16926);

        Assert.Equal(id, Assert.Single(matches).ExistingCompetitionId);
    }

    [Fact]
    public async Task Preview_FindsExisting_ByOrgCompId_WhenPoolDiffers()
    {
        // Ровно c1973: имя то же, в базе 50m, парсер без бассейна в протоколе дал 25m.
        await using var db = CreateDb(nameof(Preview_FindsExisting_ByOrgCompId_WhenPoolDiffers));
        var id = await SeedSingleAsync(db, 16926, pool: "50m");

        var svc = new JsonImportService(db, new NullCache());
        var withId = await svc.FindExistingCompetitionsAsync([Day("Лига юг тур 2", "21/11/2026", "25m")], orgCompId: 16926);
        Assert.Equal(id, Assert.Single(withId).ExistingCompetitionId);

        // Бассейн неизвестен вовсе — по compID всё равно находится.
        var noPool = await svc.FindExistingCompetitionsAsync([Day("Лига юг тур 2", "21/11/2026", null)], orgCompId: 16926);
        Assert.Equal(id, Assert.Single(noPool).ExistingCompetitionId);
    }

    [Fact]
    public async Task Preview_Event_EachFileDay_MatchesItsOwnDay()
    {
        await using var db = CreateDb(nameof(Preview_Event_EachFileDay_MatchesItsOwnDay));
        var ev = new CompetitionEvent { Name = "Событие", OrgCompId = 555 };
        db.CompetitionEvents.Add(ev);
        await db.SaveChangesAsync();
        // Штамп на соревновании — только у первого дня (AK уникален), у события — на всём.
        var d1 = new Competition { Name = "Событие", SubName = "День А", Date = "01/06/2026", PoolType = "50m", EventId = ev.Id, OrgCompId = 555, DayNumber = 1 };
        var d2 = new Competition { Name = "Событие", SubName = "День Б", Date = "02/06/2026", PoolType = "50m", EventId = ev.Id, DayNumber = 2 };
        var d3 = new Competition { Name = "Событие", SubName = "День В", Date = "03/06/2026", PoolType = "50m", EventId = ev.Id, DayNumber = 3 };
        db.Competitions.AddRange(d1, d2, d3);
        await db.SaveChangesAsync();

        var matches = await new JsonImportService(db, new NullCache()).FindExistingCompetitionsAsync(
        [
            Day("Другое имя", "03/06/2026"),
            Day("Другое имя", "01/06/2026"),
            Day("Другое имя", "02/06/2026"),
            Day("Другое имя", "04/06/2026") // дня в базе нет — новый
        ], orgCompId: 555);

        Assert.Equal([d3.Id, d1.Id, d2.Id, (int?)null], matches.Select(m => m.ExistingCompetitionId).ToArray());
    }

    [Fact]
    public async Task Preview_NewCompetition_NotFound()
    {
        await using var db = CreateDb(nameof(Preview_NewCompetition_NotFound));
        await SeedSingleAsync(db, 16926);

        var svc = new JsonImportService(db, new NullCache());
        // Другой compID, другое имя — новое соревнование.
        var other = await svc.FindExistingCompetitionsAsync([Day("Новый старт", "21/11/2026")], orgCompId: 17000);
        Assert.Null(Assert.Single(other).ExistingCompetitionId);

        // Без compID (файл на /Admin/Import) — по-прежнему только ключ по имени.
        var noId = await svc.FindExistingCompetitionsAsync([Day("Лига юг — тур 2 (исправлено)", "21/11/2026")]);
        Assert.Null(Assert.Single(noId).ExistingCompetitionId);
    }

    [Fact]
    public async Task Repull_WithDivergedNameAndPool_PreviewArmsOverwrite_ImportUpserts_NoDuplicate()
    {
        await using var db = CreateDb(nameof(Repull_WithDivergedNameAndPool_PreviewArmsOverwrite_ImportUpserts_NoDuplicate));
        var svc = new JsonImportService(db, new NullCache());

        // Первый «Затянуть»: человек выбрал 50m, compID проштампован.
        var first = await svc.ImportAsync(
            ToStream(new[] { Item("Лига юг тур 2", "21/11/2026", "A", 1), Item("Лига юг тур 2", "21/11/2026", "B", 2) }),
            "file.json",
            eventOptions: new ImportEventOptions(null, null, false, false, PoolType: "50m"),
            orgCompId: 16926);
        Assert.Empty(first.ErrorMessages);
        var compId = (await db.Competitions.SingleAsync()).Id;

        // Федерация поправила заголовок, время у B — новое; бассейн парсер снова не знает (25m).
        const string newName = "Лига юг тур 2 — итог";
        var preview = await svc.FindExistingCompetitionsAsync([Day(newName, "21/11/2026", "25m")], orgCompId: 16926);
        var existingId = Assert.Single(preview).ExistingCompetitionId;
        Assert.Equal(compId, existingId);

        // «Перезатянуть»: страница взводит перезапись только при existingCompetitionId — он есть.
        var overwrite = existingId != null;
        var repull = await svc.ImportAsync(
            ToStream(new[] { Item(newName, "21/11/2026", "A", 1), Item(newName, "21/11/2026", "B", 2, "00:29.50") }),
            "file.json",
            eventOptions: new ImportEventOptions(null, null, overwrite, false),
            orgCompId: 16926);

        Assert.DoesNotContain("Дубль", repull.Message);
        Assert.Empty(repull.ErrorMessages);
        var comp = await db.Competitions.SingleAsync();
        Assert.Equal(compId, comp.Id);
        Assert.Equal("50m", comp.PoolType); // бассейн человека не сброшен умолчанием парсера
        Assert.Equal(2, await db.Results.CountAsync(r => r.CompetitionId == compId));
        Assert.Contains(await db.Results.Select(r => r.TimeOriginal).ToListAsync(), t => t != null && t.Contains("29.5"));
    }

    [Fact]
    public async Task PlainPull_OfAlreadyPulled_WithoutOverwrite_StillRefusesAsDuplicate()
    {
        // Обычное «Затянуть» (без перезаписи) уже затянутого не перезаписывает молча.
        await using var db = CreateDb(nameof(PlainPull_OfAlreadyPulled_WithoutOverwrite_StillRefusesAsDuplicate));
        var svc = new JsonImportService(db, new NullCache());
        await svc.ImportAsync(ToStream(new[] { Item("Лига юг тур 2", "21/11/2026", "A", 1) }), "file.json", orgCompId: 16926);

        var again = await svc.ImportAsync(
            ToStream(new[] { Item("Другое имя", "21/11/2026", "A", 1, "00:28.00") }), "file.json", orgCompId: 16926);

        Assert.Contains("Дубль", again.Message);
        Assert.Equal(1, await db.Competitions.CountAsync());
        Assert.DoesNotContain(await db.Results.Select(r => r.TimeOriginal).ToListAsync(), t => t != null && t.Contains("28.0"));
    }
}
