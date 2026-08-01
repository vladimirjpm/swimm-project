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
/// Тесты склейки клубов-дублей (ClubMergeService, docs/tasks/club-merge-plan.md, фаза B):
/// перенос связей, оба guard-а, dry-run, дозаполнение полей, инвалидация кэша.
/// </summary>
public class ClubMergeServiceTests
{
    private sealed class FakeCache : ICacheService
    {
        public int InvalidateCount { get; private set; }
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() { InvalidateCount++; return Task.CompletedTask; }
    }

    /// <summary>Шпион пересчёта клубного зачёта: merge обязан его дёрнуть, иначе в
    /// ClubCompetitionStandings останутся места склеенного клуба.</summary>
    private sealed class StandingSpy : IClubStandingService
    {
        public List<int> RebuiltClubs { get; } = [];
        public List<int> RebuiltCompetitions { get; } = [];
        public Task<int> RebuildForCompetitionAsync(int competitionId, CancellationToken ct = default)
        { RebuiltCompetitions.Add(competitionId); return Task.FromResult(0); }
        public Task<int> RebuildAllAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RebuildForClubAsync(int clubId, CancellationToken ct = default)
        { RebuiltClubs.Add(clubId); return Task.FromResult(0); }
    }

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static AppUser NewUser(string email) =>
        new() { Email = email, DisplayName = email, SecurityStamp = "s" };

    private static ResultRecord NewResult(Swimmer s, Club c, Competition comp, Style st, string distance = "50") =>
        new()
        {
            Swimmer = s, Competition = comp, Style = st, Distance = distance,
            Club = c, Gender = "male", CompetitionDate = new DateTime(2026, 6, 1)
        };

    // ── Перенос всех связей + дозаполнение + удаление дубля ─────────────────

    [Fact]
    public async Task Merge_MovesAllLinks_BackfillsFields_DeletesDuplicate()
    {
        await using var db = CreateDb(nameof(Merge_MovesAllLinks_BackfillsFields_DeletesDuplicate));
        var country = new Country { CountryCode = "ISR", CountryName = "Israel" };
        var canon = new Club { Name = "הפועל דולפין נתניה" };                 // NameEn пуст, CountryId пуст
        var dup = new Club { Name = "Hapoel Dolphine Netanya", Country = country };
        var comp = new Competition { Name = "Meet", Date = "01/06/2026", PoolType = "25m" };
        var style = new Style { Name = "Freestyle" };
        var owner = NewUser("o@x.com");
        var swimmer = new Swimmer { LastName = "L", FirstName = "F", BirthYear = 2010, Club = dup };
        var group = new HubGroup { Name = "G", Slug = "g", Club = dup, Owner = owner };
        db.AddRange(country, canon, dup, comp, style, owner, swimmer, group);
        db.Results.Add(NewResult(swimmer, dup, comp, style));
        db.HubGroupClubRequests.Add(new HubGroupClubRequest { HubGroup = group, User = owner, Club = dup });
        db.UserFavorites.Add(new UserFavorite { User = owner, TargetType = "club", Club = dup });
        await db.SaveChangesAsync();

        var cache = new FakeCache();
        var report = await new ClubMergeService(db, cache, new StandingSpy())
            .MergeAsync([new ClubMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal("merged", report.Pairs.Single().Status);
        // Мягкое слияние: строка дубля ОСТАЁТСЯ со ссылкой на приёмника — иначе ссылки
        // на старый Id (в т.ч. /clubs/{id}) гниют после каждой чистки.
        Assert.Equal(canon.Id, (await db.Clubs.FindAsync(dup.Id))!.MergedIntoId);
        Assert.Equal(canon.Id, (await db.Results.SingleAsync()).ClubId);
        Assert.Equal(canon.Id, (await db.Swimmers.SingleAsync()).ClubId);
        Assert.Equal(canon.Id, (await db.HubGroups.SingleAsync()).ClubId);
        Assert.Equal(canon.Id, (await db.HubGroupClubRequests.SingleAsync()).ClubId);
        Assert.Equal(canon.Id, (await db.UserFavorites.SingleAsync()).ClubId);

        // Кросс-скрипт: латинское название дубля → NameEn канона; страна дозаполнена.
        var merged = await db.Clubs.SingleAsync(c => c.Id == canon.Id);
        Assert.Equal("Hapoel Dolphine Netanya", merged.NameEn);
        Assert.Equal(country.Id, merged.CountryId);

        Assert.Equal(1, cache.InvalidateCount);
    }

    // ── Guard 1: официальные группы у обоих ─────────────────────────────────

    [Fact]
    public async Task Merge_BothHaveOfficialGroups_ConflictAndNoChanges()
    {
        await using var db = CreateDb(nameof(Merge_BothHaveOfficialGroups_ConflictAndNoChanges));
        var canon = new Club { Name = "A" };
        var dup = new Club { Name = "B" };
        var owner = NewUser("o@x.com");
        db.AddRange(canon, dup, owner,
            new HubGroup { Name = "GA", Slug = "ga", Club = canon, IsOfficial = true, Owner = owner },
            new HubGroup { Name = "GB", Slug = "gb", Club = dup, IsOfficial = true, Owner = owner });
        await db.SaveChangesAsync();

        var cache = new FakeCache();
        var report = await new ClubMergeService(db, cache, new StandingSpy())
            .MergeAsync([new ClubMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal("conflict", report.Pairs.Single().Status);
        Assert.NotNull(await db.Clubs.FindAsync(dup.Id));                  // дубль жив
        Assert.Equal(dup.Id, (await db.HubGroups.SingleAsync(g => g.Slug == "gb")).ClubId);
        Assert.Equal(0, cache.InvalidateCount);
    }

    [Fact]
    public async Task Merge_OnlyDuplicateHasOfficialGroup_Merges()
    {
        await using var db = CreateDb(nameof(Merge_OnlyDuplicateHasOfficialGroup_Merges));
        var canon = new Club { Name = "A" };
        var dup = new Club { Name = "B" };
        var owner = NewUser("o@x.com");
        db.AddRange(canon, dup, owner,
            new HubGroup { Name = "GB", Slug = "gb", Club = dup, IsOfficial = true, Owner = owner });
        await db.SaveChangesAsync();

        var report = await new ClubMergeService(db, new FakeCache(), new StandingSpy())
            .MergeAsync([new ClubMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal("merged", report.Pairs.Single().Status);
        Assert.Equal(canon.Id, (await db.HubGroups.SingleAsync()).ClubId); // официальная группа переехала
    }

    // ── Guard 2: дедуп избранного ───────────────────────────────────────────

    [Fact]
    public async Task Merge_UserHasBothClubsInFavorites_KeepsOneRow()
    {
        await using var db = CreateDb(nameof(Merge_UserHasBothClubsInFavorites_KeepsOneRow));
        var canon = new Club { Name = "A" };
        var dup = new Club { Name = "B" };
        var user = NewUser("u@x.com");
        db.AddRange(canon, dup, user,
            new UserFavorite { User = user, TargetType = "club", Club = canon },
            new UserFavorite { User = user, TargetType = "club", Club = dup });
        await db.SaveChangesAsync();

        var report = await new ClubMergeService(db, new FakeCache(), new StandingSpy())
            .MergeAsync([new ClubMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal("merged", report.Pairs.Single().Status);
        var fav = await db.UserFavorites.SingleAsync();                    // осталась одна строка
        Assert.Equal(canon.Id, fav.ClubId);
    }

    // ── Dry-run ничего не пишет ─────────────────────────────────────────────

    [Fact]
    public async Task DryRun_ReportsPlan_WritesNothing()
    {
        await using var db = CreateDb(nameof(DryRun_ReportsPlan_WritesNothing));
        var canon = new Club { Name = "A" };
        var dup = new Club { Name = "B", NameEn = "B en" };
        var swimmer = new Swimmer { LastName = "L", FirstName = "F", BirthYear = 2010, Club = dup };
        db.AddRange(canon, dup, swimmer);
        await db.SaveChangesAsync();

        var cache = new FakeCache();
        var report = await new ClubMergeService(db, cache, new StandingSpy())
            .MergeAsync([new ClubMergePair(canon.Id, dup.Id)]);            // dryRun по умолчанию

        Assert.True(report.DryRun);
        Assert.Equal("dry-run", report.Pairs.Single().Status);
        Assert.Contains(report.Pairs.Single().Actions, a => a.StartsWith("Swimmers: 1"));

        Assert.NotNull(await db.Clubs.FindAsync(dup.Id));                  // дубль жив
        Assert.Equal(dup.Id, (await db.Swimmers.SingleAsync()).ClubId);    // связи не тронуты
        Assert.Equal("", (await db.Clubs.SingleAsync(c => c.Id == canon.Id)).NameEn);
        Assert.Equal(0, cache.InvalidateCount);                            // кэш не сброшен
    }

    // ── Синтетика защищена ──────────────────────────────────────────────────

    [Fact]
    public async Task Merge_SyntheticClub_Error()
    {
        await using var db = CreateDb(nameof(Merge_SyntheticClub_Error));
        var canon = new Club { Name = "A" };
        var synth = new Club { Name = "SYNTH Club 7" };
        db.AddRange(canon, synth);
        await db.SaveChangesAsync();

        var report = await new ClubMergeService(db, new FakeCache(), new StandingSpy())
            .MergeAsync([new ClubMergePair(canon.Id, synth.Id)], dryRun: false);

        Assert.Equal("error", report.Pairs.Single().Status);
        Assert.NotNull(await db.Clubs.FindAsync(synth.Id));
    }

    // ── Общий канон разрешён; цепочки и повторные дубли — нет ───────────────

    [Fact]
    public async Task Merge_SharedCanonical_MergesAllDuplicates()
    {
        await using var db = CreateDb(nameof(Merge_SharedCanonical_MergesAllDuplicates));
        // Типовой прогон мусора: один чистый клуб принимает несколько хвостов.
        var canon = new Club { Name = "בני הרצליה" };
        var dup1 = new Club { Name = "בני הרצליה 6.4 SW /" };
        var dup2 = new Club { Name = "בני הרצליה DNS" };
        var user = NewUser("u@x.com");
        db.AddRange(canon, dup1, dup2, user,
            new UserFavorite { User = user, TargetType = "club", Club = dup1 },
            new UserFavorite { User = user, TargetType = "club", Club = dup2 });
        await db.SaveChangesAsync();

        var report = await new ClubMergeService(db, new FakeCache(), new StandingSpy())
            .MergeAsync([new ClubMergePair(canon.Id, dup1.Id), new ClubMergePair(canon.Id, dup2.Id)], dryRun: false);

        Assert.All(report.Pairs, p => Assert.Equal("merged", p.Status));
        Assert.Equal(canon.Id, (await db.Clubs.FindAsync(dup1.Id))!.MergedIntoId);
        Assert.Equal(canon.Id, (await db.Clubs.FindAsync(dup2.Id))!.MergedIntoId);
        // Избранное схлопнулось в одну строку даже при общем каноне в одном вызове.
        var fav = await db.UserFavorites.SingleAsync();
        Assert.Equal(canon.Id, fav.ClubId);
    }

    [Fact]
    public async Task Merge_ChainedPairs_Rejected()
    {
        await using var db = CreateDb(nameof(Merge_ChainedPairs_Rejected));
        var a = new Club { Name = "A" };
        var b = new Club { Name = "B" };
        var c = new Club { Name = "C" };
        db.AddRange(a, b, c);
        await db.SaveChangesAsync();

        // b — дубль в первой паре и канон во второй (цепочка).
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ClubMergeService(db, new FakeCache(), new StandingSpy())
                .MergeAsync([new ClubMergePair(a.Id, b.Id), new ClubMergePair(b.Id, c.Id)], dryRun: false));
    }

    [Fact]
    public async Task Merge_RepeatedDuplicate_Rejected()
    {
        await using var db = CreateDb(nameof(Merge_RepeatedDuplicate_Rejected));
        var a = new Club { Name = "A" };
        var b = new Club { Name = "B" };
        var c = new Club { Name = "C" };
        db.AddRange(a, b, c);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ClubMergeService(db, new FakeCache(), new StandingSpy())
                .MergeAsync([new ClubMergePair(a.Id, c.Id), new ClubMergePair(b.Id, c.Id)], dryRun: false));
    }

    // ── Мягкое слияние (K2) ─────────────────────────────────────────────────

    [Fact]
    public async Task Merge_RebuildsClubStandingsOfCanonical()
    {
        // Иначе в ClubCompetitionStandings останутся места клуба, которого больше нет.
        await using var db = CreateDb(nameof(Merge_RebuildsClubStandingsOfCanonical));
        var canon = new Club { Name = "A" };
        var dup = new Club { Name = "B" };
        db.AddRange(canon, dup);
        await db.SaveChangesAsync();

        var spy = new StandingSpy();
        await new ClubMergeService(db, new FakeCache(), spy)
            .MergeAsync([new ClubMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal([canon.Id], spy.RebuiltClubs);
    }

    [Fact]
    public async Task Merge_AlsoRebuildsStandingsWhereDuplicateStood()
    {
        // Пересчёта только по соревнованиям канона мало: 2026-08-01 после склейки 68 дублей
        // ТРИ строки исчезнувших клубов пережили merge и остались висеть в зачёте.
        // Единицы зачёта дубля собираются ДО переноса результатов — потом связь не найти.
        await using var db = CreateDb(nameof(Merge_AlsoRebuildsStandingsWhereDuplicateStood));
        var canon = new Club { Name = "A" };
        var dup = new Club { Name = "B" };
        var comp = new Competition { Name = "Meet", Date = "01/06/2026", PoolType = "25m" };
        db.AddRange(canon, dup, comp);
        await db.SaveChangesAsync();
        db.Add(new ClubCompetitionStanding
        {
            CompetitionId = comp.Id, ClubId = dup.Id, Rank = 3,
            Points = 10, Gold = 1, Silver = 0, Bronze = 0,
            SwimmerCount = 2, ScoringSwims = 2, SwimCount = 2,
        });
        await db.SaveChangesAsync();

        var spy = new StandingSpy();
        await new ClubMergeService(db, new FakeCache(), spy)
            .MergeAsync([new ClubMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal([comp.Id], spy.RebuiltCompetitions);
    }

    [Fact]
    public async Task DryRun_DoesNotRebuildStandings()
    {
        await using var db = CreateDb(nameof(DryRun_DoesNotRebuildStandings));
        var canon = new Club { Name = "A" };
        var dup = new Club { Name = "B" };
        db.AddRange(canon, dup);
        await db.SaveChangesAsync();

        var spy = new StandingSpy();
        await new ClubMergeService(db, new FakeCache(), spy)
            .MergeAsync([new ClubMergePair(canon.Id, dup.Id)], dryRun: true);

        Assert.Empty(spy.RebuiltClubs);
        Assert.Null((await db.Clubs.FindAsync(dup.Id))!.MergedIntoId);
    }

    [Fact]
    public async Task AlreadyMergedClub_CannotBeMergedAgain()
    {
        // Повтор дал бы цепочку A → B → C, и /clubs/{A} пришлось бы разматывать рекурсивно.
        await using var db = CreateDb(nameof(AlreadyMergedClub_CannotBeMergedAgain));
        var canon = new Club { Name = "A" };
        var dup = new Club { Name = "B" };
        var third = new Club { Name = "C" };
        db.AddRange(canon, dup, third);
        await db.SaveChangesAsync();
        await new ClubMergeService(db, new FakeCache(), new StandingSpy())
            .MergeAsync([new ClubMergePair(canon.Id, dup.Id)], dryRun: false);

        var report = await new ClubMergeService(db, new FakeCache(), new StandingSpy())
            .MergeAsync([new ClubMergePair(third.Id, dup.Id)], dryRun: false);

        Assert.Equal("error", report.Pairs.Single().Status);
        Assert.Equal(canon.Id, (await db.Clubs.FindAsync(dup.Id))!.MergedIntoId);
    }

    [Fact]
    public async Task MergedClub_IsHiddenFromDedupCandidates()
    {
        await using var db = CreateDb(nameof(MergedClub_IsHiddenFromDedupCandidates));
        var canon = new Club { Name = "מכבי חיפה" };
        var dup = new Club { Name = "מכבי חיפה " };   // тот же клуб с хвостом-пробелом
        db.AddRange(canon, dup);
        await db.SaveChangesAsync();
        await new ClubMergeService(db, new FakeCache(), new StandingSpy())
            .MergeAsync([new ClubMergePair(canon.Id, dup.Id)], dryRun: false);

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        Assert.DoesNotContain(report.Candidates, c => c.DuplicateId == dup.Id || c.CanonicalId == dup.Id);
    }
}
