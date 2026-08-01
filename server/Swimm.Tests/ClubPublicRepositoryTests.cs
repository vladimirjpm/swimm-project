using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="ClubPublicRepository"/> — ростер клуба и клубные рекорды (K4.2,
/// docs/plans/club-page-plan.md). Ось рекордов скопирована с «рекордов группы»
/// (HubGroupPublicRepository, фаза 8.3) + дополнительный фильтр SuspectReason.
/// </summary>
public class ClubPublicRepositoryTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ClubPublicRepository Repo(SwimmReadDbContext db) => new(db);

    private static Swimmer MakeSwimmer(
        Club club, string lastNameEn, string firstNameEn, int birthYear, string? gender = "M") => new()
    {
        Club = club,
        LastName = lastNameEn,
        FirstName = firstNameEn,
        LastNameEn = lastNameEn,
        FirstNameEn = firstNameEn,
        BirthYear = birthYear,
        Gender = gender
    };

    /// <summary>StyleId = 100 «freestyle» — сеять ОДИН раз на тест (см. <see cref="Swim"/>),
    /// иначе несколько ResultRecord со своим Style-объектом одного Id дают конфликт трекера EF.</summary>
    private static Style FreestyleStyle() => new() { Id = 100, Name = "freestyle" };

    private static ResultRecord Swim(
        Competition comp, Club club, Swimmer swimmer, DateTime date,
        int? timeMs = 60_000, string distance = "100", string gender = "male",
        bool timeFail = false, int? relayId = null, string? suspectReason = null,
        int styleId = 100) => new()
    {
        Competition = comp,
        Club = club,
        Swimmer = swimmer,
        StyleId = styleId,
        RelayId = relayId,
        CompetitionDate = date,
        Distance = distance,
        Gender = gender,
        TimeFail = timeFail,
        TimeMillisecond = timeFail ? null : timeMs,
        TimeOriginal = timeFail ? "" : $"{timeMs / 1000.0:0.00}",
        SuspectReason = suspectReason,
        InternationalPoints = 700
    };

    private static Competition MakeCompetition(string poolType, string date = "15/02/2026") =>
        new() { Name = "Meet", Date = date, PoolType = poolType };

    /* ─────────────────────────── Ростер ─────────────────────────── */

    [Fact]
    public async Task Roster_Paginates()
    {
        using var db = CreateDb(nameof(Roster_Paginates));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        for (var i = 0; i < 5; i++)
            db.Add(MakeSwimmer(club, $"Swimmer{i}", "X", 2010));
        await db.SaveChangesAsync();

        var page1 = await Repo(db).GetRosterAsync(club.Id, page: 1, pageSize: 2, gender: null, ageFrom: null, ageTo: null, season: null);
        var page3 = await Repo(db).GetRosterAsync(club.Id, page: 3, pageSize: 2, gender: null, ageFrom: null, ageTo: null, season: null);

        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Data.Count);
        Assert.True(page1.HasMore);
        Assert.Single(page3.Data); // последняя страница — 1 запись из 5
        Assert.False(page3.HasMore);
    }

    [Fact]
    public async Task Roster_FiltersByGender()
    {
        using var db = CreateDb(nameof(Roster_FiltersByGender));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        db.Add(MakeSwimmer(club, "Male", "One", 2010, gender: "M"));
        db.Add(MakeSwimmer(club, "Female", "One", 2010, gender: "F"));
        await db.SaveChangesAsync();

        var result = await Repo(db).GetRosterAsync(club.Id, 1, 50, gender: "female", ageFrom: null, ageTo: null, season: null);

        var row = Assert.Single(result.Data);
        Assert.Equal("Female", row.LastNameEn);
        Assert.Equal("female", row.Gender); // API отдаёт male/female, не M/F
    }

    [Fact]
    public async Task Roster_FiltersByGender_WhenStoredAsWords()
    {
        // В БД Swimmer.Gender живёт в ДВУХ форматах: "male"/"female" (подавляющее
        // большинство — 2475/1484 на 2026-08-01) и "M"/"F" (13/2 от старого импорта).
        // Фильтр обязан ловить оба, иначе ростер по полу почти всегда пуст.
        using var db = CreateDb(nameof(Roster_FiltersByGender_WhenStoredAsWords));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        db.Add(MakeSwimmer(club, "Male", "One", 2010, gender: "male"));
        db.Add(MakeSwimmer(club, "Female", "One", 2010, gender: "female"));
        db.Add(MakeSwimmer(club, "Nobody", "One", 2010, gender: "none"));
        await db.SaveChangesAsync();

        var females = await Repo(db).GetRosterAsync(club.Id, 1, 50, "female", null, null, null);
        var males = await Repo(db).GetRosterAsync(club.Id, 1, 50, "male", null, null, null);

        Assert.Equal("Female", Assert.Single(females.Data).LastNameEn);
        Assert.Equal("Male", Assert.Single(males.Data).LastNameEn);
    }

    [Fact]
    public async Task Roster_FiltersByAge_UsingGivenSeason()
    {
        using var db = CreateDb(nameof(Roster_FiltersByAge_UsingGivenSeason));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        db.Add(MakeSwimmer(club, "Young", "One", 2015)); // возраст в сезоне 2025 = 10
        db.Add(MakeSwimmer(club, "Old", "One", 2010));   // возраст в сезоне 2025 = 15
        await db.SaveChangesAsync();

        var result = await Repo(db).GetRosterAsync(club.Id, 1, 50, gender: null, ageFrom: 10, ageTo: 10, season: 2025);

        var row = Assert.Single(result.Data);
        Assert.Equal("Young", row.LastNameEn);
        Assert.Equal(10, row.Age);
    }

    [Fact]
    public async Task Roster_Age_ComputedFromGivenSeason_NotToday()
    {
        using var db = CreateDb(nameof(Roster_Age_ComputedFromGivenSeason_NotToday));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        db.Add(MakeSwimmer(club, "Swimmer", "One", 2000));
        await db.SaveChangesAsync();

        // season=2020 передан явно — возраст обязан считаться от него, а не от сегодняшней даты.
        var result = await Repo(db).GetRosterAsync(club.Id, 1, 50, gender: null, ageFrom: null, ageTo: null, season: 2020);

        Assert.Equal(20, Assert.Single(result.Data).Age);
    }

    [Fact]
    public async Task Roster_CountsCompetitionsAndSwims_ScopedToClubAndOptionallySeason()
    {
        using var db = CreateDb(nameof(Roster_CountsCompetitionsAndSwims_ScopedToClubAndOptionallySeason));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = MakeSwimmer(club, "Swimmer", "One", 2010);
        db.AddRange(club, swimmer);
        var compA = MakeCompetition("25m", "15/02/2026");
        var compB = MakeCompetition("25m", "20/07/2026");
        db.AddRange(compA, compB, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(compA, club, swimmer, new DateTime(2026, 2, 15), distance: "50"),
            Swim(compA, club, swimmer, new DateTime(2026, 2, 15), distance: "100"), // тот же старт, второй заплыв
            Swim(compB, club, swimmer, new DateTime(2026, 7, 20), distance: "50")); // другой старт, но вне сезона 2025
        await db.SaveChangesAsync();

        var noSeason = await Repo(db).GetRosterAsync(club.Id, 1, 50, null, null, null, season: null);
        var row = Assert.Single(noSeason.Data);
        Assert.Equal(2, row.Competitions);
        Assert.Equal(3, row.Swims);

        // Сезон 2025 (1 сен 2025 – 31 авг 2026) включает ОБА старта — уточним границей поуже.
        var seasonScoped = await Repo(db).GetRosterAsync(club.Id, 1, 50, null, null, null, season: 2025);
        var scopedRow = Assert.Single(seasonScoped.Data);
        Assert.Equal(2, scopedRow.Competitions);
        Assert.Equal(3, scopedRow.Swims);
    }

    [Fact]
    public async Task Roster_Season_ExcludesSwimsOutsideRange()
    {
        using var db = CreateDb(nameof(Roster_Season_ExcludesSwimsOutsideRange));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = MakeSwimmer(club, "Swimmer", "One", 2010);
        db.AddRange(club, swimmer);
        var compInSeason = MakeCompetition("25m", "15/02/2026");
        var compBeforeSeason = MakeCompetition("25m", "15/02/2025");
        db.AddRange(compInSeason, compBeforeSeason, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(compInSeason, club, swimmer, new DateTime(2026, 2, 15)),
            Swim(compBeforeSeason, club, swimmer, new DateTime(2025, 2, 15))); // сезон 2024, не 2025
        await db.SaveChangesAsync();

        var result = await Repo(db).GetRosterAsync(club.Id, 1, 50, null, null, null, season: 2025);

        var row = Assert.Single(result.Data);
        Assert.Equal(1, row.Competitions);
        Assert.Equal(1, row.Swims);
    }

    /* ─────────────────────────── Рекорды ─────────────────────────── */

    [Fact]
    public async Task Records_25mAnd50m_AreDifferentRows()
    {
        using var db = CreateDb(nameof(Records_25mAnd50m_AreDifferentRows));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = MakeSwimmer(club, "Swimmer", "One", 2010);
        db.AddRange(club, swimmer);
        var comp25 = MakeCompetition("25m");
        var comp50 = MakeCompetition("50m");
        db.AddRange(comp25, comp50, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(comp25, club, swimmer, new DateTime(2026, 2, 15), timeMs: 55_000),
            Swim(comp50, club, swimmer, new DateTime(2026, 2, 15), timeMs: 58_000));
        await db.SaveChangesAsync();

        var both = await Repo(db).GetRecordsAsync(club.Id, poolType: null);
        Assert.Equal(2, both.Data.Count);
        Assert.Contains(both.Data, r => r.PoolType == "25m" && r.TimeMs == 55_000);
        Assert.Contains(both.Data, r => r.PoolType == "50m" && r.TimeMs == 58_000);

        var only25 = await Repo(db).GetRecordsAsync(club.Id, poolType: "25m");
        var row = Assert.Single(only25.Data);
        Assert.Equal("25m", row.PoolType);
        Assert.Equal(55_000, row.TimeMs);
    }

    [Fact]
    public async Task Records_ExcludeTimeFail_Relays_And_SuspectReason()
    {
        using var db = CreateDb(nameof(Records_ExcludeTimeFail_Relays_And_SuspectReason));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmerFail = MakeSwimmer(club, "Fail", "One", 2010);
        var swimmerRelay = MakeSwimmer(club, "Relay", "One", 2010);
        var swimmerSuspect = MakeSwimmer(club, "Suspect", "One", 2010);
        var swimmerValid = MakeSwimmer(club, "Valid", "One", 2010);
        db.AddRange(club, swimmerFail, swimmerRelay, swimmerSuspect, swimmerValid);
        var comp = MakeCompetition("25m");
        var relay = new Relay { TeamName = "Alpha A" };
        db.AddRange(comp, relay, FreestyleStyle());
        await db.SaveChangesAsync();

        // Три «испорченных» строки нарочно БЫСТРЕЕ валидной — если фильтр не сработает,
        // рекордом станет одна из них, а не единственная валидная запись.
        db.AddRange(
            Swim(comp, club, swimmerFail, new DateTime(2026, 2, 15), timeMs: 50_000, timeFail: true),
            Swim(comp, club, swimmerRelay, new DateTime(2026, 2, 15), timeMs: 51_000, relayId: relay.Id),
            Swim(comp, club, swimmerSuspect, new DateTime(2026, 2, 15), timeMs: 52_000, suspectReason: "protocol-error"),
            Swim(comp, club, swimmerValid, new DateTime(2026, 2, 15), timeMs: 60_000));
        await db.SaveChangesAsync();

        var result = await Repo(db).GetRecordsAsync(club.Id, poolType: null);

        var row = Assert.Single(result.Data);
        Assert.Equal(swimmerValid.Id, row.SwimmerId);
        Assert.Equal(60_000, row.TimeMs);
    }

    [Fact]
    public async Task Records_TieBreak_EarlierSwimWins()
    {
        using var db = CreateDb(nameof(Records_TieBreak_EarlierSwimWins));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var earlier = MakeSwimmer(club, "Earlier", "One", 2010);
        var later = MakeSwimmer(club, "Later", "One", 2010);
        db.AddRange(club, earlier, later);
        var comp = MakeCompetition("25m");
        db.AddRange(comp, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(comp, club, later, new DateTime(2026, 2, 20), timeMs: 55_000),
            Swim(comp, club, earlier, new DateTime(2026, 2, 10), timeMs: 55_000)); // то же время, раньше дата
        await db.SaveChangesAsync();

        var result = await Repo(db).GetRecordsAsync(club.Id, poolType: null);

        var row = Assert.Single(result.Data);
        Assert.Equal(earlier.Id, row.SwimmerId);
    }

    /* ──────────────────────── Резолв клуба ──────────────────────── */

    [Fact]
    public async Task ResolveClubIdAsync_UnknownClub_ReturnsNull()
    {
        using var db = CreateDb(nameof(ResolveClubIdAsync_UnknownClub_ReturnsNull));
        Assert.Null(await Repo(db).ResolveClubIdAsync(9999));
    }

    [Fact]
    public async Task ResolveClubIdAsync_PseudoClub_ReturnsNull()
    {
        using var db = CreateDb(nameof(ResolveClubIdAsync_PseudoClub_ReturnsNull));
        var pseudo = new Club { Name = "Israel", NameEn = "Israel", IsPseudo = true };
        db.Add(pseudo);
        await db.SaveChangesAsync();

        Assert.Null(await Repo(db).ResolveClubIdAsync(pseudo.Id));
    }

    [Fact]
    public async Task ResolveClubIdAsync_MergedClub_ReturnsReceiverId_AndReceiverDataIsServed()
    {
        using var db = CreateDb(nameof(ResolveClubIdAsync_MergedClub_ReturnsReceiverId_AndReceiverDataIsServed));
        var receiver = new Club { Name = "Beta", NameEn = "Beta" };
        db.Add(receiver);
        await db.SaveChangesAsync();
        var merged = new Club { Name = "Alpha", NameEn = "Alpha", MergedIntoId = receiver.Id };
        var swimmer = MakeSwimmer(receiver, "Swimmer", "One", 2010);
        db.AddRange(merged, swimmer);
        await db.SaveChangesAsync();

        var resolved = await Repo(db).ResolveClubIdAsync(merged.Id);
        Assert.Equal(receiver.Id, resolved);

        var roster = await Repo(db).GetRosterAsync(resolved!.Value, 1, 50, null, null, null, null);
        Assert.Single(roster.Data);
    }
}
