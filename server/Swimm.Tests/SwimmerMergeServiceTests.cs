using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты склейки пловцов-дублей (SwimmerMergeService, дедуп A2/A3).
/// EF InMemory; каждая БД — с именем теста.
/// </summary>
public class SwimmerMergeServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static Swimmer NewSwimmer(string last, string first, int year = 2015) =>
        new() { LastName = last, FirstName = first, BirthYear = year };

    private static Competition NewCompetition(string name = "Meet") =>
        new() { Name = name, Date = "01/06/2026", PoolType = "25m" };

    private static Style NewStyle(string name = "Freestyle") => new() { Name = name };

    private static ResultRecord NewResult(Swimmer s, Competition c, Style st, string distance = "50") =>
        new()
        {
            Swimmer = s, Competition = c, Style = st, Distance = distance,
            Club = new Club { Name = "Club" }, Gender = "male",
            CompetitionDate = new DateTime(2026, 6, 1)
        };

    // ── Перенос всех связей на канонического + удаление дубля ────────────────

    [Fact]
    public async Task Merge_MovesResultsAndLinks_DeletesDuplicate()
    {
        await using var db = CreateDb(nameof(Merge_MovesResultsAndLinks_DeletesDuplicate));
        var canon = NewSwimmer("סמוזיץ'", "נדב");
        var dup = NewSwimmer("סמוזיץ׳", "נדב");
        var comp1 = NewCompetition("Meet A");
        var comp2 = NewCompetition("Meet B");
        var style = NewStyle();
        var user = new AppUser { Email = "u@x.com", DisplayName = "U", SecurityStamp = "s", Swimmer = dup };
        var group = new HubGroup { Name = "G", Slug = "g" };
        db.AddRange(canon, dup, comp1, comp2, style, user, group);
        db.Results.Add(NewResult(canon, comp1, style));
        db.Results.Add(NewResult(dup, comp2, style));
        db.UserFavorites.Add(new UserFavorite { User = user, TargetType = "swimmer", Swimmer = dup });
        db.HubGroupMembers.Add(new HubGroupMember { HubGroup = group, Swimmer = dup });
        await db.SaveChangesAsync();

        var report = await new SwimmerMergeService(db)
            .MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal("merged", report.Pairs.Single().Status);
        Assert.Null(await db.Swimmers.FindAsync(dup.Id));
        Assert.All(await db.Results.ToListAsync(), r => Assert.Equal(canon.Id, r.SwimmerId));
        Assert.Equal(canon.Id, (await db.UserFavorites.SingleAsync()).SwimmerId);
        Assert.Equal(canon.Id, (await db.HubGroupMembers.SingleAsync()).SwimmerId);
        Assert.Equal(canon.Id, (await db.AppUsers.SingleAsync()).SwimmerId);
    }

    // ── Конфликт: общий заплыв → отказ, ничего не меняется ──────────────────

    [Fact]
    public async Task Merge_SharedRace_ConflictAndNoChanges()
    {
        await using var db = CreateDb(nameof(Merge_SharedRace_ConflictAndNoChanges));
        var canon = NewSwimmer("A", "X");
        var dup = NewSwimmer("B", "X");
        var comp = NewCompetition();
        var style = NewStyle();
        db.AddRange(canon, dup, comp, style);
        db.Results.Add(NewResult(canon, comp, style, "100"));
        db.Results.Add(NewResult(dup, comp, style, "100"));
        await db.SaveChangesAsync();

        var report = await new SwimmerMergeService(db)
            .MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal("conflict", report.Pairs.Single().Status);
        Assert.NotEmpty(report.Pairs.Single().Conflicts);
        Assert.NotNull(await db.Swimmers.FindAsync(dup.Id));
        Assert.Equal(1, await db.Results.CountAsync(r => r.SwimmerId == dup.Id));
    }

    // ── Dry-run: план есть, БД не изменена ───────────────────────────────────

    [Fact]
    public async Task DryRun_ReportsPlan_ChangesNothing()
    {
        await using var db = CreateDb(nameof(DryRun_ReportsPlan_ChangesNothing));
        var canon = NewSwimmer("A", "X");
        var dup = NewSwimmer("B", "X");
        var comp = NewCompetition();
        var style = NewStyle();
        db.AddRange(canon, dup, comp, style);
        db.Results.Add(NewResult(dup, comp, style));
        await db.SaveChangesAsync();

        var report = await new SwimmerMergeService(db)
            .MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)]); // dryRun по умолчанию

        var pair = report.Pairs.Single();
        Assert.True(report.DryRun);
        Assert.Equal("dry-run", pair.Status);
        Assert.Contains(pair.Actions, a => a.StartsWith("Results: 1"));
        Assert.NotNull(await db.Swimmers.FindAsync(dup.Id));
        Assert.Equal(dup.Id, (await db.Results.SingleAsync()).SwimmerId);
    }

    // ── Дубль-членство/фаворит у канонического → строка дубля удаляется ─────

    [Fact]
    public async Task Merge_DuplicateMembershipAndFavorite_RowsRemoved()
    {
        await using var db = CreateDb(nameof(Merge_DuplicateMembershipAndFavorite_RowsRemoved));
        var canon = NewSwimmer("A", "X");
        var dup = NewSwimmer("B", "X");
        var user = new AppUser { Email = "u@x.com", DisplayName = "U", SecurityStamp = "s" };
        var group = new HubGroup { Name = "G", Slug = "g" };
        db.AddRange(canon, dup, user, group);
        db.UserFavorites.Add(new UserFavorite { User = user, TargetType = "swimmer", Swimmer = canon });
        db.UserFavorites.Add(new UserFavorite { User = user, TargetType = "swimmer", Swimmer = dup });
        db.HubGroupMembers.Add(new HubGroupMember { HubGroup = group, Swimmer = canon });
        db.HubGroupMembers.Add(new HubGroupMember { HubGroup = group, Swimmer = dup });
        await db.SaveChangesAsync();

        var report = await new SwimmerMergeService(db)
            .MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal("merged", report.Pairs.Single().Status);
        Assert.Equal(canon.Id, (await db.UserFavorites.SingleAsync()).SwimmerId);
        Assert.Equal(canon.Id, (await db.HubGroupMembers.SingleAsync()).SwimmerId);
    }

    // ── Ноги эстафет: перенос на канонического (иначе FK Restrict роняет удаление) ──

    [Fact]
    public async Task Merge_MovesRelayLegs_ToCanonical()
    {
        await using var db = CreateDb(nameof(Merge_MovesRelayLegs_ToCanonical));
        var canon = NewSwimmer("A", "X");
        var dup = NewSwimmer("B", "X");
        var relay = new Relay { TeamName = "Team" };
        db.AddRange(canon, dup, relay);
        db.RelayMembers.Add(new RelayMember { Relay = relay, Swimmer = dup, LegOrder = 2 });
        await db.SaveChangesAsync();

        var report = await new SwimmerMergeService(db)
            .MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal("merged", report.Pairs.Single().Status);
        Assert.Null(await db.Swimmers.FindAsync(dup.Id));
        Assert.Equal(canon.Id, (await db.RelayMembers.SingleAsync()).SwimmerId);
    }

    [Fact]
    public async Task Merge_DuplicateRelayLegOnSameRelay_RowRemoved()
    {
        await using var db = CreateDb(nameof(Merge_DuplicateRelayLegOnSameRelay_RowRemoved));
        var canon = NewSwimmer("A", "X");
        var dup = NewSwimmer("B", "X");
        var relay = new Relay { TeamName = "Team" };
        db.AddRange(canon, dup, relay);
        db.RelayMembers.Add(new RelayMember { Relay = relay, Swimmer = canon, LegOrder = 1 });
        db.RelayMembers.Add(new RelayMember { Relay = relay, Swimmer = dup, LegOrder = 2 });
        await db.SaveChangesAsync();

        var report = await new SwimmerMergeService(db)
            .MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal("merged", report.Pairs.Single().Status);
        var leg = await db.RelayMembers.SingleAsync();   // строка дубля удалена, осталась одна
        Assert.Equal(canon.Id, leg.SwimmerId);
    }

    // ── Ошибки: синтетика, отсутствующий пловец, self-merge ──────────────────

    [Fact]
    public async Task Merge_SyntheticOrMissingOrSelf_Errors()
    {
        await using var db = CreateDb(nameof(Merge_SyntheticOrMissingOrSelf_Errors));
        var synth = NewSwimmer("Synth", "S");
        synth.SwimmerOrgId = "SYNTH-1";
        // A2: у каждой пары свои Id — иначе сработает новая проверка пересекающихся пар
        // (один и тот же canonical/duplicate в нескольких парах), которая тестируется отдельно.
        var real1 = NewSwimmer("Real1", "R");
        var real2 = NewSwimmer("Real2", "R");
        var real3 = NewSwimmer("Real3", "R");
        db.AddRange(synth, real1, real2, real3);
        await db.SaveChangesAsync();

        var report = await new SwimmerMergeService(db).MergeAsync(
        [
            new SwimmerMergePair(real1.Id, synth.Id),
            new SwimmerMergePair(real2.Id, 424242),
            new SwimmerMergePair(real3.Id, real3.Id),
        ], dryRun: false);

        Assert.All(report.Pairs, p => Assert.Equal("error", p.Status));
        Assert.Equal(4, await db.Swimmers.CountAsync());
    }

    // ── Дозаполнение пустых полей канонического из дубля ─────────────────────

    [Fact]
    public async Task Merge_FillsEmptyCanonicalFieldsFromDuplicate()
    {
        await using var db = CreateDb(nameof(Merge_FillsEmptyCanonicalFieldsFromDuplicate));
        var club = new Club { Name = "Club" };
        var canon = NewSwimmer("A", "X");
        var dup = NewSwimmer("B", "X");
        dup.Gender = "male";
        dup.Club = club;
        dup.LastNameEn = "Last";
        dup.FirstNameEn = "First";
        dup.SwimmerOrgId = "12345";
        db.AddRange(canon, dup, club);
        await db.SaveChangesAsync();

        await new SwimmerMergeService(db)
            .MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);

        var merged = await db.Swimmers.SingleAsync(s => s.Id == canon.Id);
        Assert.Equal("male", merged.Gender);
        Assert.Equal(club.Id, merged.ClubId);
        Assert.Equal("Last", merged.LastNameEn);
        Assert.Equal("12345", merged.SwimmerOrgId);
    }

    // ── A2: пересекающиеся пары отклоняются целиком, до любых изменений в БД ────

    [Fact]
    public async Task Merge_ThrowsOnOverlappingPairs()
    {
        await using var db = CreateDb(nameof(Merge_ThrowsOnOverlappingPairs));
        var a = NewSwimmer("A", "X");
        var b = NewSwimmer("B", "X");
        var c = NewSwimmer("C", "X");
        db.AddRange(a, b, c);
        await db.SaveChangesAsync();

        var service = new SwimmerMergeService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => service.MergeAsync(
        [
            new SwimmerMergePair(a.Id, b.Id),
            new SwimmerMergePair(c.Id, b.Id),
        ], dryRun: false));

        Assert.Equal(3, await db.Swimmers.CountAsync());
        Assert.NotNull(await db.Swimmers.FindAsync(a.Id));
        Assert.NotNull(await db.Swimmers.FindAsync(b.Id));
        Assert.NotNull(await db.Swimmers.FindAsync(c.Id));
    }

    [Fact]
    public async Task Merge_ThrowsWhenSameCanonicalIdUsedTwice()
    {
        await using var db = CreateDb(nameof(Merge_ThrowsWhenSameCanonicalIdUsedTwice));
        var a = NewSwimmer("A", "X");
        var b = NewSwimmer("B", "X");
        var c = NewSwimmer("C", "X");
        db.AddRange(a, b, c);
        await db.SaveChangesAsync();

        var service = new SwimmerMergeService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => service.MergeAsync(
        [
            new SwimmerMergePair(a.Id, b.Id),
            new SwimmerMergePair(a.Id, c.Id),
        ], dryRun: false));

        Assert.Equal(3, await db.Swimmers.CountAsync());
    }
}
