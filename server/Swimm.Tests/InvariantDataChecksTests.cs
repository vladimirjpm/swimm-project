using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services.DataChecks;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Проверки-инварианты фазы Д4 (docs/data-integrity.md). Каждая закрывает дыру, которую
/// до неё не ловил никто.
/// </summary>
public class InvariantDataChecksTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<(Competition comp, Style style, Club club, Swimmer swimmer)> SeedAsync(SwimmDbContext db)
    {
        var comp = new Competition { Name = "Meet", Date = "01/06/2026", PoolType = "25m" };
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Club" };
        var swimmer = new Swimmer { LastName = "Коэн", FirstName = "Таль", BirthYear = 2012, Gender = "male" };
        db.AddRange(comp, style, club, swimmer);
        await db.SaveChangesAsync();
        return (comp, style, club, swimmer);
    }

    [Fact]
    public async Task UpsertKeyCollision_FindsRowsIndistinguishableForReimport()
    {
        // И8: две строки ОДНОГО пловца, неразличимые для матчера. Время разное, поэтому
        // «точные дубликаты» (И10) их не ловят.
        // Ключ БЕЗ пловца совпадает массово и законно (заплывы нумеруются внутри возрастной
        // ступени) — третья строка ниже это и проверяет: другой пловец находкой не считается.
        await using var db = CreateDb(nameof(UpsertKeyCollision_FindsRowsIndistinguishableForReimport));
        var (comp, style, club, swimmer) = await SeedAsync(db);

        ResultRecord Row(string time, int lane) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "50", Gender = "male", Heat = 1, Lane = lane, TimeOriginal = time,
            CompetitionDate = new DateTime(2026, 6, 1)
        };
        db.Results.AddRange(Row("00:30.00", 4), Row("00:31.00", 4));  // один пловец, один ключ
        db.Results.Add(Row("00:32.00", 5));                            // своя дорожка — не находка

        // Тот же ключ (заплыв 1, дорожка 4), но ДРУГОЙ пловец — норма, а не находка.
        var other = new Swimmer { LastName = "Леви", FirstName = "Дан", BirthYear = 2013, Gender = "male" };
        db.Swimmers.Add(other);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = other.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "50", Gender = "male", Heat = 1, Lane = 4, TimeOriginal = "00:33.00",
            CompetitionDate = new DateTime(2026, 6, 1)
        });
        await db.SaveChangesAsync();

        var outcome = await new UpsertKeyCollisionCheck(db).RunAsync();

        Assert.Equal(1, outcome.Total);
        Assert.Contains("строк 2", Assert.Single(outcome.Items).Message);
    }

    [Fact]
    public async Task MergedSessions_FlagsCompetitionWhereSwimmerHasTwoPlacedSwims()
    {
        // Чемпионат «мокдамот и финал»: PDF-экспорт слил утреннюю и вечернюю сессии в один
        // список, и финалист занимает два места подряд. Официально это два зачёта с
        // отдельными медалями и очками (loglig держит их разными событиями), у нас — один.
        await using var db = CreateDb(nameof(MergedSessions_FlagsCompetitionWhereSwimmerHasTwoPlacedSwims));
        var (comp, style, club, swimmer) = await SeedAsync(db);

        ResultRecord Row(int? position, string? time, int? ms, int heat, int lane, int? swimmerId = null) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmerId ?? swimmer.Id, ClubId = club.Id,
            StyleId = style.Id, Distance = "50", Gender = "male", EventStyleAge = "14",
            Position = position, TimeOriginal = time ?? string.Empty, TimeMillisecond = ms,
            Heat = heat, Lane = lane, CompetitionDate = new DateTime(2026, 6, 1)
        };

        // Утро и вечер: обе строки с местом и временем — это и есть склейка.
        db.Results.AddRange(Row(1, "00:26.62", 26620, 4, 4), Row(2, "00:26.63", 26630, 1, 4));

        // Калибровка: «снятие + результат» у другого пловца находкой быть НЕ должно —
        // этим занимается UpsertKeyCollisionCheck, и severity там мягче.
        var other = new Swimmer { LastName = "Леви", FirstName = "Дан", BirthYear = 2012, Gender = "male" };
        db.Swimmers.Add(other);
        await db.SaveChangesAsync();
        db.Results.AddRange(
            Row(3, "00:28.00", 28000, 2, 3, other.Id),
            Row(null, null, null, 2, 3, other.Id));
        await db.SaveChangesAsync();

        var outcome = await new MergedSessionsCheck(db).RunAsync();

        // Находка одна — на СОРЕВНОВАНИЕ, а не на каждый заплыв.
        Assert.Equal(1, outcome.Total);
        var item = Assert.Single(outcome.Items);
        Assert.Equal("Competition", item.EntityType);
        Assert.Equal(comp.Id, item.EntityId);
        Assert.Contains("заплывов с дублем 1", item.Message);
    }

    /// <summary>
    /// Одна попытка, засчитанная в ДВУХ зачётах разной программы, — не склейка сессий.
    /// Живой случай — comp 1526: заплыв соблюдающих субботу печатается и в своём зачёте,
    /// и в возрастном, тем же временем. Без EventCategory в ключе проверка кричала на
    /// здоровые данные, а ложная тревога в реестре обесценивает его целиком.
    /// </summary>
    [Fact]
    public async Task MergedSessions_IgnoresSameSwimCountedInTwoProgrammes()
    {
        await using var db = CreateDb(nameof(MergedSessions_IgnoresSameSwimCountedInTwoProgrammes));
        var (comp, style, club, swimmer) = await SeedAsync(db);

        ResultRecord Row(int position, string? category) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id,
            StyleId = style.Id, Distance = "800", Gender = "male", EventStyleAge = "14",
            EventCategory = category,
            Position = position, TimeOriginal = "10:19.69", TimeMillisecond = 619690,
            Heat = 1, Lane = position, CompetitionDate = new DateTime(2026, 6, 1)
        };

        // Возрастной зачёт и шабатный: время одно, места свои — так печатает протокол.
        db.Results.AddRange(Row(6, null), Row(2, "mix-shabbat"));
        await db.SaveChangesAsync();

        var outcome = await new MergedSessionsCheck(db).RunAsync();

        Assert.Equal(0, outcome.Total);
        Assert.Empty(outcome.Items);
    }

    /// <summary>
    /// Эталон официальных очков: расхождение с нашим расчётом ловится построчно и
    /// суммируется на соревнование. Строки без эталона (все PDF-импорты) проверку не будят.
    /// </summary>
    [Fact]
    public async Task OfficialClubPoints_FlagsCompetitionWhereOurRuleDisagrees()
    {
        await using var db = CreateDb(nameof(OfficialClubPoints_FlagsCompetitionWhereOurRuleDisagrees));
        var (comp, style, club, swimmer) = await SeedAsync(db);

        var rule = new PointRuleClubs
        {
            Version = "test", EffectiveFrom = new DateOnly(2026, 1, 1), Scope = "all",
            DefaultPoints = 0, MaxScoringPlace = 3, RelayMultiplier = 2,
            Entries = [
                new PointRuleClubsEntry { Place = 1, Points = 25 },
                new PointRuleClubsEntry { Place = 2, Points = 22 },
                new PointRuleClubsEntry { Place = 3, Points = 20 }
            ]
        };
        db.PointRulesClubs.Add(rule);
        await db.SaveChangesAsync();
        comp.PointRuleClubsId = rule.Id;
        await db.SaveChangesAsync();

        ResultRecord Row(int position, string? heatType, int? official) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "50", Gender = "male", EventStyleAge = "14", Position = position,
            TimeOriginal = "00:26.62", TimeMillisecond = 26620, HeatType = heatType,
            Heat = 1, Lane = position, CompetitionDate = new DateTime(2026, 6, 1),
            OfficialClubPoints = official
        };

        db.Results.AddRange(
            Row(1, "final", 25),      // сошлось
            Row(2, "prelim", 22),     // организатор ЗАПЛАТИЛ за предварительный, мы — нет
            Row(3, "final", 0));      // организатор не заплатил за секцию, мы дали 20
        await db.SaveChangesAsync();

        var outcome = await new OfficialClubPointsMismatchCheck(db).RunAsync();

        Assert.Equal(1, outcome.Total);
        var item = Assert.Single(outcome.Items);
        Assert.Equal("Competition", item.EntityType);
        Assert.Contains("наши 45", item.Message);        // 25 + 0 (prelim) + 20
        Assert.Contains("официальные 47", item.Message); // 25 + 22 + 0
        Assert.Contains("строк с расхождением 2", item.Message);
    }

    /// <summary>Нет эталона — нет и проверки: PDF-импорты официальных очков не несут.</summary>
    [Fact]
    public async Task OfficialClubPoints_WithoutReference_IsSilent()
    {
        await using var db = CreateDb(nameof(OfficialClubPoints_WithoutReference_IsSilent));
        var (comp, style, club, swimmer) = await SeedAsync(db);
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "50", Gender = "male", Position = 1, TimeOriginal = "00:26.62",
            Heat = 1, Lane = 4, CompetitionDate = new DateTime(2026, 6, 1)
        });
        await db.SaveChangesAsync();

        Assert.Equal(0, (await new OfficialClubPointsMismatchCheck(db).RunAsync()).Total);
    }

    [Fact]
    public async Task MergedSessions_IgnoresRowsAlreadyMarkedPrelimOrFinal()
    {
        // Если сессии уже размечены (HeatType), prelim/final различимы — чинить нечего.
        await using var db = CreateDb(nameof(MergedSessions_IgnoresRowsAlreadyMarkedPrelimOrFinal));
        var (comp, style, club, swimmer) = await SeedAsync(db);

        ResultRecord Row(int position, int ms, string heatType) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "50", Gender = "male", EventStyleAge = "14", Position = position,
            TimeOriginal = "00:26.62", TimeMillisecond = ms, HeatType = heatType,
            Heat = 1, Lane = 4, CompetitionDate = new DateTime(2026, 6, 1)
        };
        db.Results.AddRange(Row(1, 26620, "final"), Row(1, 26630, "prelim"));
        await db.SaveChangesAsync();

        Assert.Equal(0, (await new MergedSessionsCheck(db).RunAsync()).Total);
    }

    [Fact]
    public async Task RelayGenderFromLeg_FlagsMixedTeamWithConcreteGender()
    {
        // И3: у смешанной команды пола нет. Помеченная male строка — след того, что пол
        // взяли с ноги; а Gender входит в ключ upsert (инцидент И-4).
        await using var db = CreateDb(nameof(RelayGenderFromLeg_FlagsMixedTeamWithConcreteGender));
        var (comp, style, club, male) = await SeedAsync(db);
        var female = new Swimmer { LastName = "Леви", FirstName = "Ноа", BirthYear = 2012, Gender = "female" };
        db.Swimmers.Add(female);
        var mixed = new Relay { TeamName = "Микс" };
        var pure = new Relay { TeamName = "Мальчики" };
        db.Relays.AddRange(mixed, pure);
        await db.SaveChangesAsync();

        db.RelayMembers.AddRange(
            new RelayMember { RelayId = mixed.Id, SwimmerId = male.Id, LegOrder = 1 },
            new RelayMember { RelayId = mixed.Id, SwimmerId = female.Id, LegOrder = 2 },
            new RelayMember { RelayId = pure.Id, SwimmerId = male.Id, LegOrder = 1 });

        ResultRecord Relay(int relayId, string gender) => new()
        {
            CompetitionId = comp.Id, SwimmerId = male.Id, ClubId = club.Id, StyleId = style.Id,
            RelayId = relayId, Distance = "4X50", Gender = gender, Heat = 1, Lane = 1,
            CompetitionDate = new DateTime(2026, 6, 1)
        };
        db.Results.AddRange(Relay(mixed.Id, "male"), Relay(pure.Id, "male"));
        await db.SaveChangesAsync();

        var outcome = await new RelayGenderFromLegCheck(db).RunAsync();

        Assert.Equal(1, outcome.Total);
        Assert.Contains("состав смешанный", Assert.Single(outcome.Items).Message);
    }

    [Fact]
    public async Task RelayGenderNone_NotFlagged()
    {
        // Правильно оформленная микс-эстафета находкой быть не должна.
        await using var db = CreateDb(nameof(RelayGenderNone_NotFlagged));
        var (comp, style, club, male) = await SeedAsync(db);
        var female = new Swimmer { LastName = "Леви", FirstName = "Ноа", BirthYear = 2012, Gender = "female" };
        db.Swimmers.Add(female);
        var relay = new Relay { TeamName = "Микс" };
        db.Relays.Add(relay);
        await db.SaveChangesAsync();
        db.RelayMembers.AddRange(
            new RelayMember { RelayId = relay.Id, SwimmerId = male.Id, LegOrder = 1 },
            new RelayMember { RelayId = relay.Id, SwimmerId = female.Id, LegOrder = 2 });
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = male.Id, ClubId = club.Id, StyleId = style.Id,
            RelayId = relay.Id, Distance = "4X50", Gender = "none", Heat = 1, Lane = 1,
            CompetitionDate = new DateTime(2026, 6, 1)
        });
        await db.SaveChangesAsync();

        Assert.Equal(0, (await new RelayGenderFromLegCheck(db).RunAsync()).Total);
    }

    [Fact]
    public async Task DuplicateEventDay_FindsTwoDaysOnSameDate()
    {
        await using var db = CreateDb(nameof(DuplicateEventDay_FindsTwoDaysOnSameDate));
        var ev = new CompetitionEvent { Name = "Событие" };
        db.CompetitionEvents.Add(ev);
        await db.SaveChangesAsync();
        db.Competitions.AddRange(
            new Competition { Name = "День 1", Date = "01/06/2026", PoolType = "25m", EventId = ev.Id },
            new Competition { Name = "День 1 (дубль)", Date = "01/06/2026", PoolType = "25m", EventId = ev.Id },
            new Competition { Name = "День 2", Date = "02/06/2026", PoolType = "25m", EventId = ev.Id });
        await db.SaveChangesAsync();

        var outcome = await new DuplicateEventDayCheck(db).RunAsync();

        Assert.Equal(1, outcome.Total);
        Assert.Contains("01/06/2026", Assert.Single(outcome.Items).Message);
    }

    [Fact]
    public async Task EmptyCompetition_FoundOnlyWhenNoResults()
    {
        await using var db = CreateDb(nameof(EmptyCompetition_FoundOnlyWhenNoResults));
        var (comp, style, club, swimmer) = await SeedAsync(db);
        var empty = new Competition { Name = "Пустое", Date = "02/06/2026", PoolType = "25m" };
        db.Competitions.Add(empty);
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "50", Gender = "male", CompetitionDate = new DateTime(2026, 6, 1)
        });
        await db.SaveChangesAsync();

        var outcome = await new EmptyCompetitionCheck(db).RunAsync();

        Assert.Equal(1, outcome.Total);
        Assert.Contains("Пустое", Assert.Single(outcome.Items).Message);
    }

    [Fact]
    public async Task NoClubPointRule_FoundOnlyForCompetitionsWithResults()
    {
        // §9.3 плана правил очков: без привязки зачёт считается подбором по дате и «едет»
        // при заведении новой версии правила. Пустое соревнование — не находка: считать
        // там нечего, а про пустоту кричит competitions.empty.
        await using var db = CreateDb(nameof(NoClubPointRule_FoundOnlyForCompetitionsWithResults));
        var (comp, style, club, swimmer) = await SeedAsync(db);

        // Проверка смотрит только на чемпионаты/мастерс/Маккабиаду (сужение 2026-08-10),
        // поэтому у подопытных стоит флаг чемпионата — иначе тест проверял бы фильтр типа,
        // а не «пустое соревнование не находка».
        comp.IsChampionship = true;
        var rule = new PointRuleClubs { Version = "2026.01", Scope = "all", EffectiveFrom = new DateOnly(2026, 1, 1) };
        db.Add(rule);
        var bound = new Competition { Name = "С правилом", Date = "03/06/2026", PoolType = "25m", IsChampionship = true };
        var empty = new Competition { Name = "Пустое без правила", Date = "02/06/2026", PoolType = "25m", IsChampionship = true };
        db.Competitions.AddRange(bound, empty);
        await db.SaveChangesAsync();

        bound.PointRuleClubsId = rule.Id;
        ResultRecord Row(int competitionId) => new()
        {
            CompetitionId = competitionId, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "50", Gender = "male", CompetitionDate = new DateTime(2026, 6, 1)
        };
        db.Results.AddRange(Row(comp.Id), Row(bound.Id));
        await db.SaveChangesAsync();

        var outcome = await new CompetitionWithoutClubPointRuleCheck(db).RunAsync();

        Assert.Equal(1, outcome.Total);
        Assert.Contains("Meet", Assert.Single(outcome.Items).Message);
    }

    [Fact]
    public async Task NoClubPointRule_SkipsCompetitionsWhereStandingsAreNotKept()
    {
        // Решение Р19: «зачёт не ведётся» — законное состояние (лиги, товарищеские старты).
        // Без пометки проверка звала чинить их наравне с настоящими пропусками и кричала волком.
        await using var db = CreateDb(nameof(NoClubPointRule_SkipsCompetitionsWhereStandingsAreNotKept));
        var (comp, style, club, swimmer) = await SeedAsync(db);

        // Чемпионат — иначе находки не было бы и без пометки, и тест ничего не проверял бы.
        comp.IsChampionship = true;
        comp.ClubPointsDisabled = true;
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "50", Gender = "male", CompetitionDate = new DateTime(2026, 6, 1)
        });
        await db.SaveChangesAsync();

        var outcome = await new CompetitionWithoutClubPointRuleCheck(db).RunAsync();

        Assert.Equal(0, outcome.Total);
        Assert.Empty(outcome.Items);
    }
}
