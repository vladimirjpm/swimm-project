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
/// соревнованием ОДНИМ правилом — <c>ImportCompetitionMatcher.MatchDay</c>: ключ (Name|SubName)|Date|PoolType,
/// при промахе — день того же compID по дате (Д2). Порядок импорта Р118 не менял.
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

    // ─── Ревью PR #269: порядок «ключ → compID» и защиты ветки compID ───

    [Fact]
    public async Task KeyPointsToX_OrgCompIdPointsToY_PreviewAndImportTakeX_WithWarning()
    {
        // (а) Имя+дата+бассейн указали на X, compID по дате — на другой Y (ошибочный штамп):
        // берётся X, как и до Р118 в импорте; Y не трогается, админ получает предупреждение.
        await using var db = CreateDb(nameof(KeyPointsToX_OrgCompIdPointsToY_PreviewAndImportTakeX_WithWarning));
        var x = new Competition { Name = "Лига юг тур 2", Date = "21/11/2026", PoolType = "25m" };
        var y = new Competition { Name = "Совсем другой старт", Date = "21/11/2026", PoolType = "25m", OrgCompId = 16926 };
        db.Competitions.AddRange(x, y);
        await db.SaveChangesAsync();
        var svc = new JsonImportService(db, new NullCache());

        var match = Assert.Single(await svc.FindExistingCompetitionsAsync([Day("Лига юг тур 2", "21/11/2026")], orgCompId: 16926));
        Assert.Equal(x.Id, match.ExistingCompetitionId);
        Assert.Contains($"#{y.Id}", match.MatchWarning);

        var result = await svc.ImportAsync(ToStream(new[] { Item("Лига юг тур 2", "21/11/2026", "A", 1) }),
            "file.json", eventOptions: new ImportEventOptions(null, null, true, true), orgCompId: 16926);

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(1, await db.Results.CountAsync(r => r.CompetitionId == x.Id));
        Assert.Equal(0, await db.Results.CountAsync(r => r.CompetitionId == y.Id));
        Assert.Contains(result.DiagnosticLog, l => l.Contains($"#{y.Id}") && l.Contains($"#{x.Id}"));
    }

    [Fact]
    public async Task Event_TwoCompIdsOnSameDate_RepullOneWithDeleteMissing_DoesNotTouchOther()
    {
        // (б) Событие из окружных протоколов (прод: e1, e7, e10, e12): дни «юг» и «север» одной
        // даты под одним EventId, у каждого свой штамп. Перезабор compID «севера» с перезаписью
        // и «удалять лишние» не должен попасть в день «юга».
        await using var db = CreateDb(nameof(Event_TwoCompIdsOnSameDate_RepullOneWithDeleteMissing_DoesNotTouchOther));
        var svc = new JsonImportService(db, new NullCache());
        await svc.ImportAsync(ToStream(new[] { Item("Юг", "21/11/2026", "South", 1) }), "south.json", orgCompId: 100);
        await svc.ImportAsync(ToStream(new[] { Item("Север", "21/11/2026", "North", 1) }), "north.json", orgCompId: 200);

        var ev = new CompetitionEvent { Name = "Событие" };
        db.CompetitionEvents.Add(ev);
        await db.SaveChangesAsync();
        var south = await db.Competitions.SingleAsync(c => c.OrgCompId == 100);
        var north = await db.Competitions.SingleAsync(c => c.OrgCompId == 200);
        foreach (var (c, sub) in new[] { (south, "Юг"), (north, "Север") })
        {
            c.EventId = ev.Id;
            c.Name = $"Событие {sub}";
            c.SubName = sub;
        }
        await db.SaveChangesAsync();

        // Заголовок «севера» на сайте поменялся — ключ промахнётся, сработает compID 200.
        var preview = Assert.Single(await svc.FindExistingCompetitionsAsync([Day("Север (итог)", "21/11/2026")], orgCompId: 200));
        Assert.Equal(north.Id, preview.ExistingCompetitionId);

        var result = await svc.ImportAsync(
            ToStream(new[] { Item("Север (итог)", "21/11/2026", "North2", 2) }),
            "north.json", eventOptions: new ImportEventOptions(null, null, true, true), orgCompId: 200);

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(2, await db.Competitions.CountAsync());
        Assert.Equal(1, await db.Results.CountAsync(r => r.CompetitionId == south.Id)); // юг цел
        var northSwimmers = await db.Results.Where(r => r.CompetitionId == north.Id)
            .Select(r => r.SwimmerId).ToListAsync();
        var north2 = await db.Swimmers.SingleAsync(s => s.LastName == "North2");
        Assert.Equal([north2.Id], northSwimmers);
    }

    [Fact]
    public async Task FileWithTwoPartsOnSameDate_NoEvent_PartsNeverMergeIntoOneCompetition()
    {
        // (в) Один файл без события, две части одной даты. Пока заголовки те же — каждая находит
        // своё по ключу; если оба поменялись, по дате их не различить: в одно соревнование
        // они не сливаются (compID по этой дате не работает, предупреждение).
        await using var db = CreateDb(nameof(FileWithTwoPartsOnSameDate_NoEvent_PartsNeverMergeIntoOneCompetition));
        var svc = new JsonImportService(db, new NullCache());
        await svc.ImportAsync(ToStream(new[]
        {
            Item("Часть 1", "21/11/2026", "A", 1), Item("Часть 2", "21/11/2026", "B", 2)
        }), "file.json", orgCompId: 300);
        Assert.Equal(2, await db.Competitions.CountAsync());

        var same = await svc.ImportAsync(ToStream(new[]
        {
            Item("Часть 1", "21/11/2026", "A", 1), Item("Часть 2", "21/11/2026", "B", 2)
        }), "file.json", eventOptions: new ImportEventOptions(null, null, true, false), orgCompId: 300);
        Assert.Empty(same.ErrorMessages);
        Assert.Equal(2, await db.Competitions.CountAsync());

        var preview = await svc.FindExistingCompetitionsAsync(
            [Day("Часть 1Б", "21/11/2026"), Day("Часть 2Б", "21/11/2026")], orgCompId: 300);
        Assert.All(preview, m => Assert.Null(m.ExistingCompetitionId));
        Assert.All(preview, m => Assert.NotNull(m.MatchWarning));

        await svc.ImportAsync(ToStream(new[]
        {
            Item("Часть 1Б", "21/11/2026", "A", 1), Item("Часть 2Б", "21/11/2026", "B", 2)
        }), "file.json", eventOptions: new ImportEventOptions(null, null, true, false), orgCompId: 300);

        // Ни в одном соревновании нет обоих пловцов сразу.
        var perComp = await db.Results.GroupBy(r => r.CompetitionId)
            .Select(g => g.Select(r => r.SwimmerId).Distinct().Count()).ToListAsync();
        Assert.All(perComp, n => Assert.Equal(1, n));
    }

    [Fact]
    public async Task EventRepull_NewDay_PreviewMarksItNew_ChosenPoolOnlyForNewDay()
    {
        // (г) Р115: событие затянуто не всё. Превью отдаёт по каждому дню, найден ли он (страница
        // требует бассейн, если есть новый). Выбранный бассейн получает только НОВЫЙ день,
        // существующие свой сохраняют.
        await using var db = CreateDb(nameof(EventRepull_NewDay_PreviewMarksItNew_ChosenPoolOnlyForNewDay));
        var ev = new CompetitionEvent { Name = "Событие", OrgCompId = 777 };
        db.CompetitionEvents.Add(ev);
        await db.SaveChangesAsync();
        var d1 = new Competition { Name = "Событие", SubName = "День 1", Date = "01/06/2026", PoolType = "25m", EventId = ev.Id, OrgCompId = 777, DayNumber = 1 };
        var d2 = new Competition { Name = "Событие", SubName = "День 2", Date = "02/06/2026", PoolType = "50m", EventId = ev.Id, DayNumber = 2 };
        db.Competitions.AddRange(d1, d2);
        await db.SaveChangesAsync();
        var svc = new JsonImportService(db, new NullCache());

        // Протокол бассейна не пишет — у парсера 25m.
        var preview = await svc.FindExistingCompetitionsAsync(
            [Day("Протокол", "01/06/2026"), Day("Протокол", "02/06/2026"), Day("Протокол", "03/06/2026")], orgCompId: 777);
        Assert.Equal([d1.Id, d2.Id, (int?)null], preview.Select(m => m.ExistingCompetitionId).ToArray());

        var result = await svc.ImportAsync(ToStream(new[]
        {
            Item("Протокол", "01/06/2026", "A", 1), Item("Протокол", "02/06/2026", "B", 1), Item("Протокол", "03/06/2026", "C", 1)
        }), "file.json", eventOptions: new ImportEventOptions(ev.Id, null, true, false, PoolType: "50m"), orgCompId: 777);

        Assert.Empty(result.ErrorMessages);
        var days = await db.Competitions.ToListAsync();
        Assert.Equal(3, days.Count);
        Assert.Equal("25m", days.Single(c => c.Id == d1.Id).PoolType); // существующий — свой
        Assert.Equal("50m", days.Single(c => c.Id == d2.Id).PoolType);
        var d3 = days.Single(c => c.Date == "03/06/2026");
        Assert.Equal("50m", d3.PoolType);                               // новый — выбранный
        Assert.Equal(ev.Id, d3.EventId);
    }

    [Fact]
    public void LinkedDayIndex_DateCollisionIsAmbiguous_ForeignStampExcluded()
    {
        // (д) Два дня на одну дату — неоднозначно, а не «первый попавшийся»; день с чужим
        // штампом в индекс не попадает.
        var a = new Competition { Id = 1, Date = "01/06/2026", OrgCompId = 10 };
        var b = new Competition { Id = 2, Date = "01/06/2026" };
        var c = new Competition { Id = 3, Date = "02/06/2026", OrgCompId = 99 };
        var d = new Competition { Id = 4, Date = "03/06/2026" };

        var index = ImportCompetitionMatcher.BuildLinkedDayIndex([a, b, c, d], 10, [], out var ambiguous);

        Assert.False(index.ContainsKey("01/06/2026"));
        Assert.Contains("01/06/2026", ambiguous);
        Assert.False(index.ContainsKey("02/06/2026"));   // чужой штамп 99
        Assert.Equal(4, index["03/06/2026"].Id);
    }
}
