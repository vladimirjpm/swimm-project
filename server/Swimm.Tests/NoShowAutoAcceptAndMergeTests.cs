using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Swimm.Infrastructure.Services.DataChecks;
using Xunit;
using static Swimm.Infrastructure.Services.DataChecks.NoShowReasons;

namespace Swimm.Tests;

/// <summary>
/// «Заявки без результата» — решение Влада 25.11.2026: соревнование, где все неявки честные,
/// принимается само (причина — в примечании, список по заявкам — в details); надёжных двойников
/// склеивает шаг импорта, остальное — человеку.
/// </summary>
public class NoShowAutoAcceptAndMergeTests
{
    // ── Реестр: своё «Принято» ставит и снимает, человеческое не трогает ────────────────────

    private static readonly DateTime At = new(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AutoAccept_OpenFinding_IsAccepted_WithPrefixedNote()
    {
        var f = new DataCheckFinding();
        DataCheckRunner.ApplyAutoAccept(f, "все неявки честные — замена в эстафете — 4", At);

        Assert.Equal(DataCheckResolutions.Accepted, f.Resolution);
        Assert.Equal("автоматически: все неявки честные — замена в эстафете — 4", f.Note);
        Assert.Equal(At, f.ResolvedAt);
    }

    [Fact]
    public void AutoAccepted_BecomesDishonest_IsReopened()
    {
        var f = new DataCheckFinding { Resolution = DataCheckResolutions.Accepted, Note = "автоматически: все неявки честные", ResolvedAt = At };
        DataCheckRunner.ApplyAutoAccept(f, null, At.AddDays(1));

        Assert.Null(f.Resolution);
        Assert.Null(f.Note);
        Assert.Null(f.ResolvedAt);
    }

    [Fact]
    public void AutoAccepted_StaysHonest_NoteIsRefreshed_DateKept()
    {
        var f = new DataCheckFinding { Resolution = DataCheckResolutions.Accepted, Note = "автоматически: старое", ResolvedAt = At };
        DataCheckRunner.ApplyAutoAccept(f, "новое", At.AddDays(1));

        Assert.Equal("автоматически: новое", f.Note);
        Assert.Equal(At, f.ResolvedAt);
    }

    [Theory]
    [InlineData("автоматически: ")]
    [InlineData(null)]
    public void HumanAccepted_IsNeverTouched(string? _)
    {
        var f = new DataCheckFinding { Resolution = DataCheckResolutions.Accepted, Note = "Честные неявки (8): сверено 19.11", ResolvedAt = At };
        DataCheckRunner.ApplyAutoAccept(f, null, At.AddDays(1));
        DataCheckRunner.ApplyAutoAccept(f, "все неявки честные", At.AddDays(1));

        Assert.Equal(DataCheckResolutions.Accepted, f.Resolution);
        Assert.Equal("Честные неявки (8): сверено 19.11", f.Note);
    }

    // ── Проверка: когда принимать саму ───────────────────────────────────────────────────

    [Fact]
    public void AutoAcceptNote_AllHonest_Accepts_WithSummary()
    {
        var note = NoShowUnmatchedCheck.AutoAcceptNote(3,
            [("a", Kind.RelaySubstitution), ("b", Kind.RelaySubstitution), ("c", Kind.Empty)]);
        Assert.Equal("все неявки честные — замена в эстафете — 2, дорожка пуста — честная неявка — 1", note);
    }

    [Theory]
    [InlineData(Kind.Twin)]
    [InlineData(Kind.Namesake)]
    [InlineData(Kind.LaneTakenNoEntry)]
    public void AutoAcceptNote_AnyDishonest_LeavesToHuman(Kind bad) =>
        Assert.Null(NoShowUnmatchedCheck.AutoAcceptNote(2, [("a", Kind.Empty), ("b", bad)]));

    [Fact]
    public void AutoAcceptNote_NotEveryNoShowExplained_LeavesToHuman() =>
        Assert.Null(NoShowUnmatchedCheck.AutoAcceptNote(3, [("a", Kind.Empty), ("b", Kind.Empty)]));

    // ── Надёжный двойник ─────────────────────────────────────────────────────────────────

    private static Person P(int id, string last, string first, int year, int? loglig = null, int? club = 506) =>
        new(id, last, first, year, loglig, club);

    private static Entry E(Person p, int club = 506) => new(p, club, "female", false, 3, 2);

    private static LaneRow Row(Person p, int club = 506) => new(p, club, "female", "00:54.69", false, []);

    // 16903: ליבמן מילי אריאל (loglig 329594, клуба нет) ← ליבמן מילי (без loglig, клуб 506).
    [Fact]
    public void ReliableTwin_ShortenedName_SameYearAndClub_OneLoglig_KeepsLogligCard()
    {
        var pair = ReliableTwin(E(P(65092, "ליבמן", "מילי אריאל", 2016, loglig: 329594, club: null)),
            [Row(P(65175, "ליבמן", "מילי", 2016))], []);
        Assert.Equal((65092, 65175), pair);
    }

    [Fact]
    public void ReliableTwin_LogligOnTheResultCard_KeepsIt() =>
        Assert.Equal((2, 1), ReliableTwin(E(P(1, "ליבמן", "מילי", 2016)), [Row(P(2, "ליבמן", "מילי אריאל", 2016, loglig: 7))], []));

    // 16899: год 2008 / 2018 — одна из карточек ошибается, решает человек.
    [Fact]
    public void ReliableTwin_YearDiffers_LeftToHuman() =>
        Assert.Null(ReliableTwin(E(P(65109, "נבו", "בן", 2008, loglig: 333527)), [Row(P(65165, "נבו", "בן", 2018))], []));

    // 16924: опечатка в фамилии — человеку.
    [Fact]
    public void ReliableTwin_Typo_LeftToHuman() =>
        Assert.Null(ReliableTwin(E(P(62172, "נצ׳ייבקסי", "אורי", 2013, loglig: 281201)), [Row(P(65162, "נצ׳ייבסקי", "אורי", 2013))], []));

    // 16926: у обоих свои loglig-id — тёзки.
    [Fact]
    public void ReliableTwin_BothHaveLoglig_Namesakes_NotMerged() =>
        Assert.Null(ReliableTwin(E(P(61243, "אדרי", "איתי", 2015, loglig: 276096)), [Row(P(6663, "אדרי", "איתי", 2015, loglig: 274690))], []));

    [Fact]
    public void ReliableTwin_NoLogligAtAll_LeftToHuman() =>
        Assert.Null(ReliableTwin(E(P(1, "ליבמן", "מילי", 2016)), [Row(P(2, "ליבמן", "מילי", 2016))], []));

    [Fact]
    public void ReliableTwin_RowOfAnotherClub_LeftToHuman() =>
        Assert.Null(ReliableTwin(E(P(1, "ליבמן", "מילי", 2016, loglig: 5)), [Row(P(2, "ליבמן", "מילי", 2016, club: 506), club: 999)], []));

    [Fact]
    public void ReliableTwin_CardsClubsDisagree_LeftToHuman() =>
        Assert.Null(ReliableTwin(E(P(1, "ליבמן", "מילי", 2016, loglig: 5, club: 506)), [Row(P(2, "ליבמן", "מילי", 2016, club: 777))], []));

    // ── Сервис склейки ───────────────────────────────────────────────────────────────────

    private sealed class World
    {
        public required SwimmDbContext Db;
        public readonly Mock<ISwimmerMergeService> Merge = new();
        public readonly Mock<IAdminAuditService> Audit = new();
        public bool Enabled = true;

        public NoShowTwinMergeService Service() => new(Db, Merge.Object, Audit.Object,
            Mock.Of<ISettingsService>(s => s.GetValue(NoShowTwinMergeRules.SettingKey, true) == Enabled),
            NullLogger<NoShowTwinMergeService>.Instance);
    }

    private static async Task<World> SeedAsync(string name)
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);
        var style = new Style { Id = 1, Name = "freestyle" };
        var club = new Club { Id = 506, Name = "C", NameEn = "C" };
        var comp = new Competition { Id = 1896, Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" }, Date = "20/11/2026", PoolType = "25m" };
        db.AddRange(style, club, comp,
            new Swimmer { Id = 65092, LastName = "ליבמן", FirstName = "מילי אריאל", BirthYear = 2016, LogligId = 329594 },
            new Swimmer { Id = 65175, LastName = "ליבמן", FirstName = "מילי", BirthYear = 2016, ClubId = 506 });
        db.CompetitionEntries.Add(new CompetitionEntry
        {
            Id = 1, OrgCompId = 16903, CompetitionId = 1896, CompDate = new DateTime(2026, 11, 20), CompName = "Meet",
            SwimmerId = 65092, ClubId = 506, StyleId = 1, Distance = "50", Gender = "female", Heat = 3, Lane = 2,
            Status = CompetitionEntryStatus.NoShow,
        });
        db.Results.Add(new ResultRecord
        {
            Id = 10, CompetitionId = 1896, SwimmerId = 65175, ClubId = 506, StyleId = 1, Distance = "50", Gender = "female",
            Heat = 3, Lane = 2, TimeOriginal = "00:54.69", TimeMillisecond = 54690, CompetitionDate = new DateTime(2026, 11, 20),
        });
        await db.SaveChangesAsync();
        return new World { Db = db };
    }

    private static SwimmerMergeReport Report(bool dry, string status, params string[] conflicts)
    {
        var r = new SwimmerMergeReport { DryRun = dry };
        var p = new SwimmerMergePairResult { CanonicalId = 65092, DuplicateId = 65175, Status = status };
        p.Conflicts.AddRange(conflicts);
        r.Pairs.Add(p);
        return r;
    }

    [Fact]
    public async Task Service_MergesReliableTwin_DryRunFirst_AndAudits()
    {
        var w = await SeedAsync(nameof(Service_MergesReliableTwin_DryRunFirst_AndAudits));
        w.Merge.Setup(m => m.MergeAsync(It.IsAny<IReadOnlyList<SwimmerMergePair>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Report(true, "dry-run"));
        w.Merge.Setup(m => m.MergeAsync(It.IsAny<IReadOnlyList<SwimmerMergePair>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Report(false, "merged"));

        var report = await w.Service().MergeAsync([1896]);

        Assert.Equal(["#65092 ← #65175 ליבמן מילי"], report.Merged);
        Assert.Empty(report.Skipped);
        w.Merge.Verify(m => m.MergeAsync(It.Is<IReadOnlyList<SwimmerMergePair>>(l => l.Single() == new SwimmerMergePair(65092, 65175)),
            false, It.IsAny<CancellationToken>()), Times.Once);
        w.Audit.Verify(a => a.LogAsync(NoShowTwinMergeRules.AuditAction, "Swimmer", "65092", It.IsAny<string>(),
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Service_ConflictInDryRun_NotApplied_ReportedAsSkipped()
    {
        var w = await SeedAsync(nameof(Service_ConflictInDryRun_NotApplied_ReportedAsSkipped));
        w.Merge.Setup(m => m.MergeAsync(It.IsAny<IReadOnlyList<SwimmerMergePair>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Report(true, "conflict", "общий заплыв"));

        var report = await w.Service().MergeAsync([1896]);

        Assert.Empty(report.Merged);
        Assert.Contains("общий заплыв", Assert.Single(report.Skipped));
        w.Merge.Verify(m => m.MergeAsync(It.IsAny<IReadOnlyList<SwimmerMergePair>>(), false, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Service_SwitchedOff_DoesNothing()
    {
        var w = await SeedAsync(nameof(Service_SwitchedOff_DoesNothing));
        w.Enabled = false;

        Assert.Empty((await w.Service().MergeAsync([1896])).Merged);
        w.Merge.VerifyNoOtherCalls();
    }
}
