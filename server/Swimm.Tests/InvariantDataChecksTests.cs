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
