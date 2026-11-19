using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Флаг «клуб по мастерс-стартам» (Р109, §7.1 docs/plans/swimmer-club-change-plan.md, Влад 19.11.2026):
/// пересчёт ставит его сам, кандидат — клуб последнего мастерс-соревнования, снятый админом флаг
/// автоматика не возвращает, сверка с loglig — как у всех (Р98). Само правило — в
/// <see cref="SwimmerCurrentClubRuleTests"/>.
/// </summary>
public class SwimmerClubMastersTests
{
    private const int Masters = 438, Regular = 449;
    private const int CardOnRegular = 7165, CardOnMasters = 7169, FlagRemoved = 7383, NoMasters = 7000;

    private static SwimmDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>
    /// Мастерс-клуб «אקוותיקים» и клуб обычных стартов. У всех мастерс-старт летом и обычный — в ноябре.
    /// 7165 — карточка на клубе обычных стартов; 7169 — на мастерс-клубе; 7383 — флаг снят админом;
    /// 7000 — без мастерс-стартов.
    /// </summary>
    private static async Task<SwimmDbContext> SeedAsync()
    {
        var db = CreateDb();
        db.Clubs.AddRange(new Club { Id = Masters, Name = "אקוותיקים" }, new Club { Id = Regular, Name = "מכבי תל אביב" });
        db.Styles.Add(new Style { Id = 1, Name = "Freestyle" });
        db.Competitions.AddRange(
            new Competition { Id = 10, Name = "Masters", Date = "2026-06-01", IsMasters = true },
            new Competition { Id = 11, Name = "League", Date = "2026-11-05" });
        db.Swimmers.AddRange(
            new Swimmer { Id = CardOnRegular, LastName = "א", FirstName = "א", ClubId = Regular, LogligId = 501, LogligIdStatus = "Verified" },
            new Swimmer { Id = CardOnMasters, LastName = "ב", FirstName = "ב", ClubId = Masters },
            new Swimmer { Id = FlagRemoved, LastName = "ג", FirstName = "ג", ClubId = Masters, ClubFromMasters = false },
            new Swimmer { Id = NoMasters, LastName = "ד", FirstName = "ד", ClubId = Masters });
        long id = 1;
        foreach (var s in new[] { CardOnRegular, CardOnMasters, FlagRemoved })
        {
            db.Results.Add(Row(id++, s, Masters, 10, "2026-06-01"));
            db.Results.Add(Row(id++, s, Regular, 11, "2026-11-05"));
        }
        db.Results.Add(Row(id, NoMasters, Regular, 11, "2026-11-05"));
        await db.SaveChangesAsync();
        return db;
    }

    private static ResultRecord Row(long id, int swimmer, int club, int competition, string date) => new()
    {
        Id = id, SwimmerId = swimmer, ClubId = club, CompetitionId = competition, StyleId = 1,
        CompetitionDate = DateTime.Parse(date), Distance = "50", Gender = "male",
    };

    [Fact]
    public async Task DryRun_PlansMastersClub_WritesNoFlag()
    {
        await using var db = await SeedAsync();

        var report = await new SwimmerClubRefreshService(db).RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: false);

        var plan = report.Changes.ToDictionary(c => c.SwimmerId);
        Assert.Equal((Masters, true), (plan[CardOnRegular].ToClubId, plan[CardOnRegular].FromMasters));
        Assert.False(plan.ContainsKey(CardOnMasters));                       // обычный старт не двигает мастерс-клуб
        Assert.Equal((Regular, false), (plan[FlagRemoved].ToClubId, plan[FlagRemoved].FromMasters));
        Assert.Equal(Regular, plan[NoMasters].ToClubId);
        Assert.Equal((2, 2), (report.MastersFlagged, report.FromMasters));
        Assert.All(await db.Swimmers.AsNoTracking().Where(s => s.Id != FlagRemoved).ToListAsync(), s => Assert.Null(s.ClubFromMasters));
        Assert.Empty(db.SwimmerClubCandidates);
    }

    [Fact]
    public async Task Apply_SetsFlag_KeepsAdminRemoval_CandidateIsMastersClub()
    {
        await using var db = await SeedAsync();

        await new SwimmerClubRefreshService(db).RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);

        var flags = await db.Swimmers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.ClubFromMasters);
        Assert.Equal(true, flags[CardOnRegular]);
        Assert.Equal(true, flags[CardOnMasters]);
        Assert.Equal(false, flags[FlagRemoved]);   // снятый админом флаг автоматика не возвращает
        Assert.Null(flags[NoMasters]);

        var candidates = await db.SwimmerClubCandidates.AsNoTracking().ToDictionaryAsync(c => c.SwimmerId, c => c.ToClubId);
        Assert.Equal(Masters, candidates[CardOnRegular]);
        Assert.False(candidates.ContainsKey(CardOnMasters));
        // Карточку сам пересчёт по-прежнему не меняет (Р98).
        Assert.Equal(Regular, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == CardOnRegular)).ClubId);
    }

    [Fact]
    public async Task Flag_WithdrawsPendingRegularClubCandidate()
    {
        // До Р109 мастерсу завели кандидата на клуб обычных стартов — флаг его снимает.
        await using var db = await SeedAsync();
        db.SwimmerClubCandidates.Add(new SwimmerClubCandidate
        {
            SwimmerId = CardOnMasters, FromClubId = Masters, ToClubId = Regular, LastSwimDate = new DateOnly(2026, 11, 5),
            Source = SwimmerClubChangeSources.Import, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var report = await new SwimmerClubRefreshService(db).RefreshAsync([CardOnMasters], SwimmerClubChangeSources.Import, apply: true);

        Assert.Equal(1, report.Withdrawn);
        Assert.Empty(db.SwimmerClubCandidates);
    }

    [Fact]
    public async Task Admin_RemovesAndRestoresFlag_NullLeavesIt()
    {
        await using var db = await SeedAsync();
        var svc = new SwimmerClubRefreshService(db);
        await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);

        var (removed, _) = await svc.SetByAdminAsync(CardOnMasters, Masters, locked: false, clubFromMasters: false);
        Assert.Equal((true, false), (removed!.WasFromMasters, removed.FromMasters));
        await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);
        Assert.False((await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == CardOnMasters)).ClubFromMasters);
        // Без флага правило снова считает все старты: кандидат — клуб обычного старта.
        Assert.Equal(Regular, (await db.SwimmerClubCandidates.AsNoTracking().SingleAsync(c => c.SwimmerId == CardOnMasters)).ToClubId);

        await svc.SetByAdminAsync(CardOnMasters, Masters, locked: false);   // null — флаг не трогаем
        Assert.False((await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == CardOnMasters)).ClubFromMasters);

        await svc.SetByAdminAsync(CardOnMasters, Masters, locked: false, clubFromMasters: true);
        Assert.True((await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == CardOnMasters)).ClubFromMasters);
    }

    [Fact]
    public async Task AdminCard_ShowsMastersRule()
    {
        await using var db = await SeedAsync();

        var dto = (await new SwimmerClubRefreshService(db).GetForAdminAsync(CardOnRegular))!;

        Assert.Null(dto.ClubFromMasters);          // ещё не поставлен, но действует (есть мастерс-старты)
        Assert.True(dto.RuleFromMasters);
        Assert.Equal(Masters, dto.RuleClubId);
    }

    [Fact]
    public async Task Dashboard_ListsFlaggedWithRegularStartsElsewhere()
    {
        await using var db = await SeedAsync();
        var svc = new SwimmerClubRefreshService(db);
        await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);

        var summary = await svc.MastersAsync(20);

        // Флаг у 7165 и 7169; обычные старты за другой клуб, чем в карточке, — только у 7169
        // (у 7165 карточка и есть клуб обычных стартов).
        Assert.Equal((2, 1), (summary.Flagged, summary.Mixed));
        var item = Assert.Single(summary.Items);
        Assert.Equal((CardOnMasters, "אקוותיקים"), (item.SwimmerId, item.ClubName));
        Assert.Equal(["מכבי תל אביב"], item.OtherClubs);
    }

    [Fact]
    public async Task Loglig_ShowsRegularClub_CandidateWaits()
    {
        // М3: сверка как у всех. loglig показывает клуб обычных стартов (= клуб карточки) —
        // мастерс-клуб не подтверждён, карточка не меняется, кандидат ждёт.
        await using var db = await SeedAsync();
        await new SwimmerClubRefreshService(db).RefreshAsync([CardOnRegular], SwimmerClubChangeSources.Cli, apply: true);
        var loglig = new ImportClubEvidenceTests.FakeLoglig(new() { [501] = "מכבי תל אביב" });

        var report = await new SwimmerClubLogligCheckService(db, loglig,
            NullLogger<SwimmerClubLogligCheckService>.Instance, TimeSpan.Zero).CheckPendingAsync(SwimmerClubCheckScope.AllPending);

        Assert.Equal(SwimmerClubLogligVerdicts.StillOld, Assert.Single(report.Verdicts).Verdict);
        Assert.Equal(0, report.Applied);
        Assert.Equal(Regular, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == CardOnRegular)).ClubId);
        Assert.Single(db.SwimmerClubCandidates);
    }
}
