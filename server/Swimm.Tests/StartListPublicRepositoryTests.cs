using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Публичный стартовый протокол (docs/plans/start-list-plan.md, шаг С6): три уровня
/// приближения одного набора заявок — программа дня, заплыв, карточка пловца.
/// </summary>
public class StartListPublicRepositoryTests
{
    private const int OrgCompId = 16786;

    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<SwimmReadDbContext> SeedAsync(string name)
    {
        var db = CreateDb(name);

        var club = new Club { Id = 1, Name = "Дельфин Нетания" };
        var other = new Club { Id = 2, Name = "Другой клуб" };
        var free = new Style { Id = 1, Name = "freestyle" };
        var back = new Style { Id = 2, Name = "backstroke" };
        db.AddRange(club, other, free, back);

        db.Swimmers.AddRange(
            new Swimmer { Id = 10, LastName = "Тестовый", FirstName = "Пловец", LastNameEn = "Test", FirstNameEn = "Swimmer", BirthYear = 2016 },
            new Swimmer { Id = 11, LastName = "Второй", FirstName = "Пловец", BirthYear = 2016 },
            new Swimmer { Id = 12, LastName = "Третий", FirstName = "Пловец", BirthYear = 2016 });

        // День 1: два заплыва одной дисциплины (76321) + другая дисциплина (76322).
        db.CompetitionEntries.AddRange(
            Entry(1, 76321, ev: 5, styleId: 1, dist: "50", heat: 1, lane: 4, swimmer: 10, club: 1,
                at: new DateTime(2026, 2, 19, 8, 6, 0, DateTimeKind.Utc), seed: "01:42.72"),
            Entry(2, 76321, ev: 5, styleId: 1, dist: "50", heat: 2, lane: 5, swimmer: 11, club: 2,
                at: new DateTime(2026, 2, 19, 8, 9, 0, DateTimeKind.Utc), seed: ""),
            Entry(3, 76322, ev: 8, styleId: 2, dist: "100", heat: 1, lane: 3, swimmer: 10, club: 1,
                at: new DateTime(2026, 2, 19, 9, 20, 0, DateTimeKind.Utc), seed: ""),
            // День 2 — многодневка: дата другая.
            Entry(4, 76401, ev: 40, styleId: 1, dist: "4X50", heat: 1, lane: 6, swimmer: 12, club: 1,
                at: new DateTime(2026, 2, 20, 10, 0, 0, DateTimeKind.Utc), seed: "",
                day: new DateTime(2026, 2, 20)));

        await db.SaveChangesAsync();
        return db;
    }

    private static CompetitionEntry Entry(
        long id, int disciplineId, int ev, int styleId, string dist, int heat, int lane,
        int swimmer, int club, DateTime at, string seed, DateTime? day = null) =>
        new()
        {
            Id = id,
            OrgCompId = OrgCompId,
            CompDate = day ?? new DateTime(2026, 2, 19),
            CompName = "Чемпионат",
            OrgDisciplineId = disciplineId,
            OrgEventNumber = ev,
            SwimmerId = swimmer,
            ClubId = club,
            StyleId = styleId,
            Distance = dist,
            Gender = "female",
            EventCategory = "בנות 10",
            AgeBand = "10",
            Heat = heat,
            Lane = lane,
            HeatStartAt = at,
            Round = "timed-final",
            SeedTimeOriginal = seed,
            PulledAt = new DateTime(2026, 2, 18, 20, 0, 0, DateTimeKind.Utc)
        };

    // ── Зум 1: программа ─────────────────────────────────────────────────────

    [Fact]
    public async Task Programme_GroupsByDay_AndOrdersByTime()
    {
        await using var db = await SeedAsync(nameof(Programme_GroupsByDay_AndOrdersByTime));

        var p = await new StartListPublicRepository(db).GetProgrammeAsync(OrgCompId);

        Assert.NotNull(p);
        Assert.Equal(2, p!.Days.Count);
        Assert.Equal(new DateTime(2026, 2, 19), p.Days[0].Date);
        Assert.Equal(new DateTime(2026, 2, 20), p.Days[1].Date);

        var firstDay = p.Days[0].Events;
        Assert.Equal(2, firstDay.Count);
        Assert.Equal(76321, firstDay[0].OrgDisciplineId);   // 08:06 раньше, чем 09:20
        Assert.Equal(2, firstDay[0].Entries);
        Assert.Equal(2, firstDay[0].Heats);

        // Витрина обязана показать, когда протокол последний раз подтверждён: посев меняют
        // до последнего дня, а дожать изменение до открытой страницы в проекте нечем.
        Assert.Equal(new DateTime(2026, 2, 18, 20, 0, 0, DateTimeKind.Utc), p.UpdatedAt);
        Assert.Equal(4, p.Entries);
    }

    [Fact]
    public async Task Programme_MarksRelayByDistance()
    {
        await using var db = await SeedAsync(nameof(Programme_MarksRelayByDistance));

        var p = await new StartListPublicRepository(db).GetProgrammeAsync(OrgCompId);

        // У заявки нет флага эстафеты — он выводится из дистанции «4X50»: команды источник
        // не печатает, ноги склеивает пара заплыв+дорожка.
        var relay = p!.Days[1].Events.Single();
        Assert.True(relay.IsRelay);
        Assert.False(p.Days[0].Events[0].IsRelay);
    }

    [Fact]
    public async Task Programme_UnknownCompetition_IsNull()
    {
        await using var db = await SeedAsync(nameof(Programme_UnknownCompetition_IsNull));

        Assert.Null(await new StartListPublicRepository(db).GetProgrammeAsync(999));
    }

    /// <summary>
    /// Справки о старте (Т1) может не быть вовсе, и это обычное состояние: чемпионат тогда
    /// false, разминка null — блок ARRIVE BY на витрине просто не рисуется.
    /// </summary>
    [Fact]
    public async Task Programme_WithoutMeetInfo_HasNoChampionshipAndNoWarmUp()
    {
        await using var db = await SeedAsync(nameof(Programme_WithoutMeetInfo_HasNoChampionshipAndNoWarmUp));

        var p = await new StartListPublicRepository(db).GetProgrammeAsync(OrgCompId);

        Assert.False(p!.IsChampionship);
        Assert.All(p.Days, d => Assert.Null(d.WarmUpAt));
    }

    /// <summary>
    /// Разминка ложится в СВОЙ день: у многодневки их несколько, и «приезжать к» второго дня
    /// не имеет отношения к первому. Заодно проверяем, что действующий флаг чемпионата —
    /// ручная правка, а не то, что определил забор.
    /// </summary>
    [Fact]
    public async Task Programme_CarriesWarmUpPerDay_AndOverriddenChampionship()
    {
        await using var db = await SeedAsync(nameof(Programme_CarriesWarmUpPerDay_AndOverriddenChampionship));
        db.CompetitionMeetInfos.Add(new CompetitionMeetInfo
        {
            OrgCompId = OrgCompId,
            IsChampionship = false,
            IsChampionshipOverride = true,
            WarmUps =
            {
                new CompetitionWarmUp
                {
                    OrgCompId = OrgCompId,
                    Date = new DateTime(2026, 2, 20),
                    WarmUpAt = new DateTime(2026, 2, 20, 6, 30, 0, DateTimeKind.Utc)
                }
            }
        });
        await db.SaveChangesAsync();

        var p = await new StartListPublicRepository(db).GetProgrammeAsync(OrgCompId);

        Assert.True(p!.IsChampionship);
        Assert.Null(p.Days[0].WarmUpAt);
        Assert.Equal(new DateTime(2026, 2, 20, 6, 30, 0, DateTimeKind.Utc), p.Days[1].WarmUpAt);
    }

    // ── Клубы соревнования (Т2): секция «follow a whole club» ────────────────

    [Fact]
    public async Task Clubs_CountSwimmersAndSwims_OrderedByEntries()
    {
        await using var db = await SeedAsync(nameof(Clubs_CountSwimmersAndSwims_OrderedByEntries));

        var clubs = await new StartListPublicRepository(db).GetClubsAsync([OrgCompId]);

        Assert.Equal(2, clubs.Count);

        // Клуб 1: пловцы 10 (два заплыва) и 12 (эстафета) → 2 пловца, 3 заплыва.
        Assert.Equal(1, clubs[0].ClubId);
        Assert.Equal("Дельфин Нетания", clubs[0].ClubName);
        Assert.Equal(2, clubs[0].Swimmers);
        Assert.Equal(3, clubs[0].Entries);

        // Клуб 2 меньше — значит ниже: пикер показывает первыми тех, у кого народу больше.
        Assert.Equal(2, clubs[1].ClubId);
        Assert.Equal(1, clubs[1].Swimmers);
        Assert.Equal(1, clubs[1].Entries);
    }

    /// <summary>
    /// Эстафета в источнике — ЧЕТЫРЕ строки с одной парой заплыв+дорожка. Это один заплыв
    /// команды: считать ноги порознь значит завышать счётчик клуба вчетверо (та же склейка,
    /// что `mergeRelayLanes` на витрине). А вот пловцов в ней действительно четверо.
    /// </summary>
    [Fact]
    public async Task Clubs_RelayLegsCountAsOneSwim_ButFourSwimmers()
    {
        await using var db = await SeedAsync(nameof(Clubs_RelayLegsCountAsOneSwim_ButFourSwimmers));
        db.Swimmers.AddRange(
            new Swimmer { Id = 13, LastName = "Нога", FirstName = "Вторая", BirthYear = 2016 },
            new Swimmer { Id = 14, LastName = "Нога", FirstName = "Третья", BirthYear = 2016 },
            new Swimmer { Id = 15, LastName = "Нога", FirstName = "Четвёртая", BirthYear = 2016 });
        // Те же дисциплина/заплыв/дорожка, что у строки 4 из сида — остальные ноги команды.
        db.CompetitionEntries.AddRange(
            Entry(5, 76401, ev: 40, styleId: 1, dist: "4X50", heat: 1, lane: 6, swimmer: 13, club: 1,
                at: new DateTime(2026, 2, 20, 10, 0, 0, DateTimeKind.Utc), seed: "", day: new DateTime(2026, 2, 20)),
            Entry(6, 76401, ev: 40, styleId: 1, dist: "4X50", heat: 1, lane: 6, swimmer: 14, club: 1,
                at: new DateTime(2026, 2, 20, 10, 0, 0, DateTimeKind.Utc), seed: "", day: new DateTime(2026, 2, 20)),
            Entry(7, 76401, ev: 40, styleId: 1, dist: "4X50", heat: 1, lane: 6, swimmer: 15, club: 1,
                at: new DateTime(2026, 2, 20, 10, 0, 0, DateTimeKind.Utc), seed: "", day: new DateTime(2026, 2, 20)));
        await db.SaveChangesAsync();

        var club = (await new StartListPublicRepository(db).GetClubsAsync([OrgCompId])).First(c => c.ClubId == 1);

        Assert.Equal(3, club.Entries);    // два личных + ОДНА эстафета, а не четыре
        Assert.Equal(5, club.Swimmers);   // 10, 12, 13, 14, 15
    }

    /// <summary>
    /// Составной старт: номера дисциплин принадлежат РАЗНЫМ протоколам федерации и между
    /// ними совпадают. Если ключ заплыва не включает compID, два разных заплыва схлопнутся
    /// в один и счётчик клуба занизится.
    /// </summary>
    [Fact]
    public async Task Clubs_AcrossSources_DoesNotMergeSameDisciplineNumbers()
    {
        await using var db = await SeedAsync(nameof(Clubs_AcrossSources_DoesNotMergeSameDisciplineNumbers));
        var second = Entry(8, 76321, ev: 5, styleId: 1, dist: "50", heat: 1, lane: 4, swimmer: 10, club: 1,
            at: new DateTime(2026, 2, 21, 8, 6, 0, DateTimeKind.Utc), seed: "", day: new DateTime(2026, 2, 21));
        second.OrgCompId = OrgCompId + 1;   // соседний окружной протокол того же чемпионата
        db.CompetitionEntries.Add(second);
        await db.SaveChangesAsync();

        var repo = new StartListPublicRepository(db);
        var single = (await repo.GetClubsAsync([OrgCompId])).First(c => c.ClubId == 1);
        var across = (await repo.GetClubsAsync([OrgCompId, OrgCompId + 1])).First(c => c.ClubId == 1);

        Assert.Equal(3, single.Entries);
        Assert.Equal(4, across.Entries);   // а не 3: заплывы разных протоколов не схлопнулись
        Assert.Equal(2, across.Swimmers);  // пловец один и тот же — он не удваивается
    }

    [Fact]
    public async Task Clubs_UnknownCompetition_IsEmpty()
    {
        await using var db = await SeedAsync(nameof(Clubs_UnknownCompetition_IsEmpty));

        Assert.Empty(await new StartListPublicRepository(db).GetClubsAsync([999]));
    }

    // ── Зум 2: заплыв ────────────────────────────────────────────────────────

    [Fact]
    public async Task Event_SplitsIntoHeats_WithLanesInOrder()
    {
        await using var db = await SeedAsync(nameof(Event_SplitsIntoHeats_WithLanesInOrder));

        var ev = await new StartListPublicRepository(db).GetEventAsync(OrgCompId, 76321);

        Assert.NotNull(ev);
        Assert.Equal(2, ev!.Heats.Count);
        Assert.Equal(1, ev.Heats[0].Heat);
        Assert.Equal(new DateTime(2026, 2, 19, 8, 6, 0, DateTimeKind.Utc), ev.Heats[0].StartAt);
        Assert.Equal(4, ev.Heats[0].Lanes.Single().Lane);
        Assert.Equal("timed-final", ev.Heats[0].Round);
    }

    // ── Зум 3: карточка пловца ───────────────────────────────────────────────

    [Fact]
    public async Task Swimmer_ReturnsHisSwimsInTimeOrder_WithFirstStart()
    {
        await using var db = await SeedAsync(nameof(Swimmer_ReturnsHisSwimsInTimeOrder_WithFirstStart));

        var card = await new StartListPublicRepository(db).GetSwimmerAsync(OrgCompId, 10);

        Assert.NotNull(card);
        // Имя — данные, а не интерфейс: показываем родное (у живых данных — ивритское),
        // английское остаётся фоллбеком (правило Влада 28.08.2026, см. SwimmerName).
        Assert.Equal("Пловец Тестовый", card!.SwimmerName);
        Assert.Equal(2, card.Swims.Count);
        Assert.Equal(76321, card.Swims[0].OrgDisciplineId);
        Assert.Equal(76322, card.Swims[1].OrgDisciplineId);

        // Из первого старта витрина считает «приезжать к» — минус разминка.
        Assert.Equal(new DateTime(2026, 2, 19, 8, 6, 0, DateTimeKind.Utc), card.FirstStartAt);
    }

    [Fact]
    public async Task Swimmer_SeedTimeCarriesItsQualityClass()
    {
        await using var db = await SeedAsync(nameof(Swimmer_SeedTimeCarriesItsQualityClass));

        var card = await new StartListPublicRepository(db).GetSwimmerAsync(OrgCompId, 10);

        // Посевное время — личный рекорд С ДРУГОГО старта. Показать его как результат этого
        // заплыва — ровно тот класс ошибки, ради которого написан И11.
        Assert.All(card!.Swims, s => Assert.Equal("seed", s.Quality));
        Assert.Equal("01:42.72", card.Swims[0].SeedTime);
        Assert.Null(card.Swims[1].SeedTime);               // «NT» приходит пустой строкой
    }

    [Fact]
    public async Task Swimmer_NotInThisCompetition_IsNull()
    {
        await using var db = await SeedAsync(nameof(Swimmer_NotInThisCompetition_IsNull));

        Assert.Null(await new StartListPublicRepository(db).GetSwimmerAsync(OrgCompId, 999));
    }

    /// <summary>
    /// Составное соревнование: один наш старт собран из НЕСКОЛЬКИХ compID федерации
    /// (окружные протоколы). Карточка обязана собрать заплывы пловца из всех источников
    /// и упорядочить их по дню, а не по источнику: родителю это один календарь.
    /// </summary>
    [Fact]
    public async Task SwimmerAcross_MergesSourcesAndOrdersByDay()
    {
        await using var db = await SeedAsync(nameof(SwimmerAcross_MergesSourcesAndOrdersByDay));

        // Второй источник, ДЕНЬ РАНЬШЕ: если бы порядок шёл по compID или по порядку
        // аргументов, этот заплыв встал бы в конец.
        db.CompetitionEntries.Add(new CompetitionEntry
        {
            Id = 100,
            OrgCompId = 16789,
            CompDate = new DateTime(2026, 2, 15),
            CompName = "Чемпионат — север",
            OrgDisciplineId = 77001,
            OrgEventNumber = 3,
            SwimmerId = 10,
            ClubId = 1,
            StyleId = 1,
            Distance = "50",
            Gender = "female",
            Heat = 1,
            Lane = 2,
            HeatStartAt = new DateTime(2026, 2, 15, 9, 0, 0, DateTimeKind.Utc),
            Round = "timed-final",
            SeedTimeOriginal = "",
            PulledAt = new DateTime(2026, 2, 14, 20, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var card = await new StartListPublicRepository(db)
            .GetSwimmerAcrossAsync([OrgCompId, 16789], 10);

        Assert.NotNull(card);
        Assert.Equal(3, card!.Swims.Count);
        Assert.Equal([77001, 76321, 76322], card.Swims.Select(s => s.OrgDisciplineId).ToArray());
        // Первый старт — самый ранний ПО ВСЕМ источникам, из него считается «приезжать к».
        Assert.Equal(new DateTime(2026, 2, 15, 9, 0, 0, DateTimeKind.Utc), card.FirstStartAt);
        // Каждая строка помнит СВОЙ источник: ссылка внутрь (заплыв) идёт по нему.
        Assert.Equal(16789, card.Swims[0].OrgCompId);
        Assert.Equal(OrgCompId, card.Swims[1].OrgCompId);
    }

    [Fact]
    public async Task SwimmerAcross_NoSources_IsNull()
    {
        await using var db = await SeedAsync(nameof(SwimmerAcross_NoSources_IsNull));

        Assert.Null(await new StartListPublicRepository(db).GetSwimmerAcrossAsync([], 10));
    }

    // ── Клуб и «ближайшие» ───────────────────────────────────────────────────

    [Fact]
    public async Task Club_ReturnsOnlyItsOwnSwims()
    {
        await using var db = await SeedAsync(nameof(Club_ReturnsOnlyItsOwnSwims));

        var swims = await new StartListPublicRepository(db).GetClubSwimsAsync(OrgCompId, 1);

        Assert.Equal(3, swims.Count);
        Assert.All(swims, s => Assert.Equal("Дельфин Нетания", s.ClubName));
    }

    [Fact]
    public async Task Upcoming_TakesOnlySelectedSwimmersInsideTheWindow()
    {
        await using var db = await SeedAsync(nameof(Upcoming_TakesOnlySelectedSwimmersInsideTheWindow));
        var repo = new StartListPublicRepository(db);

        var inside = await repo.GetUpcomingAsync([10], new DateTime(2026, 2, 18), 7);
        Assert.Equal(2, inside.Count);
        Assert.All(inside, s => Assert.Equal(10, s.SwimmerId));

        // Старт уже прошёл — в «ближайших» ему не место.
        var after = await repo.GetUpcomingAsync([10], new DateTime(2026, 3, 1), 7);
        Assert.Empty(after);
    }

    [Fact]
    public async Task Upcoming_KeepsEventsWithoutAssignedTime()
    {
        // Отбор идёт по дате соревнования, а не по времени заплыва: время могут ещё не
        // назначить, но родителю важно, что старт вообще есть.
        await using var db = await SeedAsync(nameof(Upcoming_KeepsEventsWithoutAssignedTime));
        var entry = await db.CompetitionEntries.FirstAsync(e => e.Id == 1);
        entry.HeatStartAt = null;
        await db.SaveChangesAsync();

        var swims = await new StartListPublicRepository(db).GetUpcomingAsync([10], new DateTime(2026, 2, 18), 7);

        Assert.Equal(2, swims.Count);
        Assert.Contains(swims, s => s.HeatStartAt is null);
    }

    [Fact]
    public async Task Upcoming_NoSwimmers_IsEmpty()
    {
        await using var db = await SeedAsync(nameof(Upcoming_NoSwimmers_IsEmpty));

        Assert.Empty(await new StartListPublicRepository(db).GetUpcomingAsync([], DateTime.UtcNow, 7));
    }

    // ── Предстоящие соревнования (решение В9) ────────────────────────────────

    [Fact]
    public async Task UpcomingCompetitions_BuiltFromEntries_WithDayAndEntryCounts()
    {
        // Список строится по ЗАЯВКАМ: у предстоящего старта своей строки в Competitions
        // ещё нет, а «Входящие» публичному пути недоступны (нет гранта swimm_ro).
        await using var db = await SeedAsync(nameof(UpcomingCompetitions_BuiltFromEntries_WithDayAndEntryCounts));

        var list = await new StartListPublicRepository(db)
            .GetUpcomingCompetitionsAsync(new DateTime(2026, 2, 18), 30);

        var comp = Assert.Single(list);
        Assert.Equal(OrgCompId, comp.OrgCompId);
        Assert.Equal("Чемпионат", comp.CompName);
        Assert.Equal(new DateTime(2026, 2, 19), comp.DateStart);
        Assert.Equal(new DateTime(2026, 2, 20), comp.DateEnd);
        Assert.Equal(2, comp.Days);
        Assert.Equal(4, comp.Entries);
        Assert.Equal(3, comp.Swimmers);
    }

    [Fact]
    public async Task UpcomingCompetitions_PastStartsAreNotListed()
    {
        await using var db = await SeedAsync(nameof(UpcomingCompetitions_PastStartsAreNotListed));

        var list = await new StartListPublicRepository(db)
            .GetUpcomingCompetitionsAsync(new DateTime(2026, 3, 1), 30);

        Assert.Empty(list);
    }

    [Fact]
    public async Task UpcomingCompetitions_WindowCutsOffFarFuture()
    {
        await using var db = await SeedAsync(nameof(UpcomingCompetitions_WindowCutsOffFarFuture));

        // Окно 18-е +1 день = по 19-е включительно: второй день многодневки (20-е) за
        // границей. Соревнование всё равно показывается — по своему первому дню внутри окна,
        // но считает только попавшие в него заявки.
        var list = await new StartListPublicRepository(db)
            .GetUpcomingCompetitionsAsync(new DateTime(2026, 2, 18), 1);

        var comp = Assert.Single(list);
        Assert.Equal(1, comp.Days);
        Assert.Equal(3, comp.Entries);
        Assert.Equal(new DateTime(2026, 2, 19), comp.DateEnd);
    }

    // ── 404-шов ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Exists_AnswersPerSlice()
    {
        await using var db = await SeedAsync(nameof(Exists_AnswersPerSlice));
        var repo = new StartListPublicRepository(db);

        Assert.True(await repo.ExistsAsync(OrgCompId));
        Assert.True(await repo.ExistsAsync(OrgCompId, orgDisciplineId: 76321));
        Assert.True(await repo.ExistsAsync(OrgCompId, swimmerId: 10));
        Assert.False(await repo.ExistsAsync(OrgCompId, orgDisciplineId: 999));
        Assert.False(await repo.ExistsAsync(999));
    }
}
