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
/// Склейка-переименование и цепочки склеек (docs/plans/swimmer-club-change-plan.md §5, Н2–Н4):
/// старое название уходит в ClubNames с датами ДО переноса, перенесённые строки помнят его,
/// английское название дубля канону не передаётся; клубы, склеенные в дубль, перевешиваются
/// на канон; разовый догон цепочек.
/// </summary>
public class ClubRenameMergeTests
{
    private sealed class NoStandings : IClubStandingService
    {
        public Task<int> RebuildForCompetitionAsync(int competitionId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RebuildAllAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RebuildForClubAsync(int clubId, CancellationToken ct = default) => Task.FromResult(0);
    }

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase($"{nameof(ClubRenameMergeTests)}.{name}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ResultRecord Row(Swimmer s, Club c, Competition comp, Style st, DateTime date) =>
        new() { Swimmer = s, Competition = comp, Style = st, Distance = "50", Club = c, Gender = "male", CompetitionDate = date };

    /// <summary>Hapoel (старое) → Aluma (новое): две строки Hapoel в разные даты, одна Aluma.</summary>
    private static async Task<(Club Aluma, Club Hapoel)> SeedHodHasharonAsync(SwimmDbContext db)
    {
        var aluma = new Club { Name = "אלומה עירוני הוד השרון", NameEn = "" };
        var hapoel = new Club { Name = "הפועל עירוני הוד השרון", NameEn = "Hapoel Hod Hasharon" };
        var comp = new Competition { Name = "Meet", Date = "25/10/2024", PoolType = "25m" };
        var style = new Style { Name = "Freestyle" };
        var swimmer = new Swimmer { LastName = "L", FirstName = "F", BirthYear = 2012, Club = hapoel };
        db.AddRange(aluma, hapoel, comp, style, swimmer,
            Row(swimmer, hapoel, comp, style, new DateTime(2024, 10, 25)),
            Row(swimmer, hapoel, comp, style, new DateTime(2026, 2, 16)),
            Row(swimmer, aluma, comp, style, new DateTime(2026, 11, 5)));
        await db.SaveChangesAsync();
        return (aluma, hapoel);
    }

    [Fact]
    public async Task Rename_StoresFormerName_WithDatesBeforeTransfer_AndMarksMovedRows()
    {
        await using var db = CreateDb(nameof(Rename_StoresFormerName_WithDatesBeforeTransfer_AndMarksMovedRows));
        var (aluma, hapoel) = await SeedHodHasharonAsync(db);

        var report = await new ClubMergeService(db, new NoStandings()).MergeAsync(
            [new ClubMergePair(aluma.Id, hapoel.Id, ClubMergeKinds.Rename)], dryRun: false);

        var pair = Assert.Single(report.Pairs);
        Assert.Equal("merged", pair.Status);
        Assert.Equal(ClubMergeKinds.Rename, pair.Kind);

        var name = Assert.Single(await db.ClubNames.ToListAsync());
        Assert.Equal(aluma.Id, name.ClubId);
        Assert.Equal("הפועל עירוני הוד השרון", name.Name);
        Assert.Equal("Hapoel Hod Hasharon", name.NameEn);
        Assert.Equal(new DateTime(2024, 10, 25), name.UsedFrom);
        Assert.Equal(new DateTime(2026, 2, 16), name.UsedTo);
        Assert.Equal(ClubNameKinds.Former, name.Kind);
        Assert.Equal(hapoel.Id, name.MergedClubId);

        // Все строки теперь у Aluma; бывшие Hapoel помнят название, своя строка Aluma — нет.
        var rows = await db.Results.OrderBy(r => r.CompetitionDate).ToListAsync();
        Assert.All(rows, r => Assert.Equal(aluma.Id, r.ClubId));
        Assert.Equal([name.Id, name.Id, (int?)null], rows.Select(r => r.ClubNameId).ToArray());

        // Английское название дубля — СТАРОЕ, канону его не передаём (при «Дубле» передали бы).
        var canon = await db.Clubs.SingleAsync(c => c.Id == aluma.Id);
        Assert.Equal("", canon.NameEn);
        Assert.Equal(aluma.Id, (await db.Clubs.SingleAsync(c => c.Id == hapoel.Id)).MergedIntoId);
    }

    [Fact]
    public async Task Duplicate_StoresNoFormerName_AndBackfillsNameEn()
    {
        await using var db = CreateDb(nameof(Duplicate_StoresNoFormerName_AndBackfillsNameEn));
        var (aluma, hapoel) = await SeedHodHasharonAsync(db);

        await new ClubMergeService(db, new NoStandings()).MergeAsync(
            [new ClubMergePair(aluma.Id, hapoel.Id)], dryRun: false);

        Assert.Empty(await db.ClubNames.ToListAsync());
        Assert.All(await db.Results.ToListAsync(), r => Assert.Null(r.ClubNameId));
        Assert.Equal("Hapoel Hod Hasharon", (await db.Clubs.SingleAsync(c => c.Id == aluma.Id)).NameEn);
    }

    [Fact]
    public async Task Rename_DryRun_WritesNothing_ButReportsFormerName()
    {
        await using var db = CreateDb(nameof(Rename_DryRun_WritesNothing_ButReportsFormerName));
        var (aluma, hapoel) = await SeedHodHasharonAsync(db);

        var report = await new ClubMergeService(db, new NoStandings()).MergeAsync(
            [new ClubMergePair(aluma.Id, hapoel.Id, ClubMergeKinds.Rename)], dryRun: true);

        Assert.Contains(report.Pairs.Single().Actions, a => a.Contains("ClubNames: прежнее название") && a.Contains("25.10.2024"));
        Assert.Empty(await db.ClubNames.AsNoTracking().ToListAsync());
        Assert.Null((await db.Clubs.AsNoTracking().SingleAsync(c => c.Id == hapoel.Id)).MergedIntoId);
    }

    [Fact]
    public async Task UnknownKind_IsError()
    {
        await using var db = CreateDb(nameof(UnknownKind_IsError));
        var (aluma, hapoel) = await SeedHodHasharonAsync(db);

        var report = await new ClubMergeService(db, new NoStandings()).MergeAsync(
            [new ClubMergePair(aluma.Id, hapoel.Id, "split")], dryRun: false);

        Assert.Equal("error", report.Pairs.Single().Status);
        Assert.Null((await db.Clubs.AsNoTracking().SingleAsync(c => c.Id == hapoel.Id)).MergedIntoId);
    }

    [Fact]
    public async Task Rename_RowAlreadyRememberingOlderName_KeepsIt()
    {
        await using var db = CreateDb(nameof(Rename_RowAlreadyRememberingOlderName_KeepsIt));
        var (aluma, hapoel) = await SeedHodHasharonAsync(db);
        // Hapoel сам когда-то назывался иначе — одна его строка помнит то, ещё более старое.
        var older = new ClubName { ClubId = hapoel.Id, Name = "הפועל הוד השרון", Kind = ClubNameKinds.Former };
        db.ClubNames.Add(older);
        await db.SaveChangesAsync();
        var first = await db.Results.OrderBy(r => r.CompetitionDate).FirstAsync();
        first.ClubNameId = older.Id;
        await db.SaveChangesAsync();

        await new ClubMergeService(db, new NoStandings()).MergeAsync(
            [new ClubMergePair(aluma.Id, hapoel.Id, ClubMergeKinds.Rename)], dryRun: false);

        var rows = await db.Results.AsNoTracking().OrderBy(r => r.CompetitionDate).ToListAsync();
        Assert.Equal(older.Id, rows[0].ClubNameId);
        // Старое название дубля переехало к канону вместе с его строками.
        var names = await db.ClubNames.AsNoTracking().ToListAsync();
        Assert.Equal(2, names.Count);
        Assert.All(names, n => Assert.Equal(aluma.Id, n.ClubId));
    }

    [Fact]
    public async Task Merge_RepointsClubsMergedIntoDuplicate_NoChains()
    {
        await using var db = CreateDb(nameof(Merge_RepointsClubsMergedIntoDuplicate_NoChains));
        var (aluma, hapoel) = await SeedHodHasharonAsync(db);
        // Прод 18.11: 605 и 447 склеены в 1372 (Hapoel) раньше.
        var old605 = new Club { Name = "605", MergedIntoId = hapoel.Id };
        var old447 = new Club { Name = "447", MergedIntoId = hapoel.Id };
        db.Clubs.AddRange(old605, old447);
        await db.SaveChangesAsync();

        var report = await new ClubMergeService(db, new NoStandings()).MergeAsync(
            [new ClubMergePair(aluma.Id, hapoel.Id, ClubMergeKinds.Rename)], dryRun: false);

        Assert.Contains(report.Pairs.Single().Actions, a => a.StartsWith("Clubs.MergedIntoId: 2"));
        var clubs = await db.Clubs.AsNoTracking().ToDictionaryAsync(c => c.Id);
        Assert.Equal(aluma.Id, clubs[old605.Id].MergedIntoId);
        Assert.Equal(aluma.Id, clubs[old447.Id].MergedIntoId);
        Assert.Equal(aluma.Id, clubs[hapoel.Id].MergedIntoId);
    }

    [Fact]
    public async Task FlattenChains_RepointsTombstonesAndNames_ToLiveClub_Idempotent()
    {
        await using var db = CreateDb(nameof(FlattenChains_RepointsTombstonesAndNames_ToLiveClub_Idempotent));
        // Цепочки как на проде: 1329 → 605 → 1372, 1361 → 447 → 1372; плюс живой 1454.
        var live = new Club { Name = "1372" };
        db.Clubs.Add(live);
        await db.SaveChangesAsync();
        var c605 = new Club { Name = "605", MergedIntoId = live.Id };
        var c447 = new Club { Name = "447", MergedIntoId = live.Id };
        db.Clubs.AddRange(c605, c447);
        await db.SaveChangesAsync();
        var c1329 = new Club { Name = "1329", MergedIntoId = c605.Id };
        var c1361 = new Club { Name = "1361", MergedIntoId = c447.Id };
        db.Clubs.AddRange(c1329, c1361);
        // Прежнее название, заведённое на клуб, который потом склеили: хозяин — надгробие.
        db.ClubNames.Add(new ClubName { ClubId = c605.Id, Name = "old 605", Kind = ClubNameKinds.Former });
        await db.SaveChangesAsync();

        var service = new ClubMergeService(db, new NoStandings());
        var dry = await service.FlattenChainsAsync(dryRun: true);
        Assert.Equal(2, dry.Moves.Count);
        Assert.Single(dry.NameMoves);
        Assert.Equal(c605.Id, (await db.Clubs.AsNoTracking().SingleAsync(c => c.Id == c1329.Id)).MergedIntoId);

        var applied = await service.FlattenChainsAsync(dryRun: false);
        Assert.Equal(2, applied.Moves.Count);
        var clubs = await db.Clubs.AsNoTracking().ToDictionaryAsync(c => c.Id);
        Assert.Equal(live.Id, clubs[c1329.Id].MergedIntoId);
        Assert.Equal(live.Id, clubs[c1361.Id].MergedIntoId);
        Assert.Equal(live.Id, (await db.ClubNames.AsNoTracking().SingleAsync()).ClubId);

        db.ChangeTracker.Clear();
        var again = await service.FlattenChainsAsync(dryRun: false);
        Assert.Empty(again.Moves);
        Assert.Empty(again.NameMoves);
    }
}
