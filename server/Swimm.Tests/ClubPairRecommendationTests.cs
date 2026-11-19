using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пара клубов в одних протоколах и вывод по паре (Р113): «скорее один клуб / переименование /
/// разные / не ясно». Все четыре спорные пары прода 19.11.2026 — разные клубы: в одних протоколах
/// отдельными командами со своим зачётом, ни один пловец не записан за оба.
/// </summary>
public class ClubPairRecommendationTests
{
    // ── Правило «в одних протоколах» ─────────────────────────────────────────

    private static ClubProtocolPresence Presence(params (long Meet, int Comp, int Day, int Swimmer, bool Standing)[] rows)
    {
        var p = new ClubProtocolPresence();
        foreach (var r in rows)
        {
            p.AddResult(r.Meet, r.Comp, new DateTime(2026, 1, r.Day), r.Swimmer, relay: false);
            if (r.Standing) p.StandingMeets.Add(r.Meet);
        }
        return p;
    }

    [Fact]
    public void Overlap_SeparateTeamsInSameMeet_TwoClubsLikely()
    {
        var a = Presence((1, 1, 1, 10, true), (2, 2, 5, 11, true), (3, 3, 9, 12, false));
        var b = Presence((1, 1, 1, 20, true), (2, 2, 5, 21, false));

        var o = ClubPairOverlapRule.Build(a, b);

        Assert.Equal((2, 1, 0), (o.Meets, o.SeparateStandings, o.SameSwimmerMeets));
        Assert.True(o.TwoClubsLikely);
    }

    [Fact]
    public void Overlap_SameSwimmerUnderBothNames_NotTwoClubs()
    {
        var a = Presence((1, 1, 1, 10, false));
        var b = Presence((1, 1, 1, 10, false));

        var o = ClubPairOverlapRule.Build(a, b);

        Assert.Equal((1, 1), (o.Meets, o.SameSwimmerMeets));
        Assert.False(o.TwoClubsLikely);
    }

    [Fact]
    public void Overlap_RelaysDoNotCountAsSameSwimmer()
    {
        var a = new ClubProtocolPresence();
        var b = new ClubProtocolPresence();
        a.AddResult(1, 1, new DateTime(2026, 1, 1), 10, relay: true);
        b.AddResult(1, 1, new DateTime(2026, 1, 1), 10, relay: true);

        Assert.Equal(0, ClubPairOverlapRule.Build(a, b).SameSwimmerMeets);
    }

    [Fact]
    public void Overlap_NeverTogether_SpansStillKnown()
    {
        var a = Presence((1, 1, 1, 10, false));
        var b = Presence((2, 2, 20, 20, false));

        var o = ClubPairOverlapRule.Build(a, b);

        Assert.Equal(0, o.Meets);
        Assert.Equal(new DateOnly(2026, 1, 20), o.SpanB!.From);
        Assert.True(o.SpansApart);
    }

    [Fact]
    public void MeetKey_MultiDayIsOneMeet_NoCollisionWithSingleDayId() =>
        Assert.NotEqual(ClubPairOverlapRule.MeetKey(57, null), ClubPairOverlapRule.MeetKey(99, 57));

    // ── Вывод по паре ────────────────────────────────────────────────────────

    private static ClubDedupCandidate Pair(ClubPairOverlap? protocols = null, ClubPairLogligSummary? loglig = null,
        bool sure = false, string heuristic = "swimmers") =>
        new(509, "הפועל ירושלים", null, 4800, 1455, "הפועל ירושלים אומנותית", null, 60,
            heuristic, 23, sure, loglig, protocols);

    private static ClubSpan Span(int fromYear, int fromMonth, int toYear, int toMonth) =>
        new(new DateOnly(fromYear, fromMonth, 1), new DateOnly(toYear, toMonth, 1));

    private static string Verdict(ClubDedupCandidate c) => ClubPairRecommendationRule.Decide(c).Verdict;

    [Fact]
    public void SameSwimmerUnderBothNames_OneClub() =>
        Assert.Equal(ClubPairVerdicts.OneClub, Verdict(Pair(new ClubPairOverlap(2, 0, 1, []))));

    [Fact]
    public void SeparateTeamsInSameProtocols_TwoClubs_EvenIfLogligSeesOneSide()
    {
        // 509/1455 на проде: 3 общих соревнования со своим зачётом, loglig — канон 17 · архив 6.
        var r = ClubPairRecommendationRule.Decide(Pair(new ClubPairOverlap(3, 3, 0, []),
            new ClubPairLogligSummary(17, 0, 0, 0, 6, 0, 0)));
        Assert.Equal(ClubPairVerdicts.TwoClubs, r.Verdict);
        Assert.Contains("свой клубный зачёт в 3", r.Reason);
    }

    [Fact]
    public void LogligSeesBothClubs_TwoClubs() =>
        Assert.Equal(ClubPairVerdicts.TwoClubs, Verdict(Pair(ClubPairOverlap.None,
            new ClubPairLogligSummary(5, 4, 0, 0, 0, 0, 0))));

    [Fact]
    public void SpansApart_Rename_TellsWhenDupIsNewer()
    {
        var dupOlder = ClubPairRecommendationRule.Decide(Pair(ClubPairOverlap.None with
            { SpanA = Span(2025, 10, 2026, 11), SpanB = Span(2024, 10, 2025, 8) }));
        Assert.Equal(ClubPairVerdicts.Rename, dupOlder.Verdict);
        Assert.DoesNotContain("местами", dupOlder.Reason);

        var dupNewer = ClubPairRecommendationRule.Decide(Pair(ClubPairOverlap.None with
            { SpanA = Span(2024, 10, 2025, 8), SpanB = Span(2025, 10, 2026, 11) }));
        Assert.Equal(ClubPairVerdicts.Rename, dupNewer.Verdict);
        Assert.Contains("местами", dupNewer.Reason);
    }

    [Fact]
    public void SureHeuristic_NeverTogether_OverlappingSpans_OneClub() =>
        Assert.Equal(ClubPairVerdicts.OneClub, Verdict(Pair(ClubPairOverlap.None with
            { SpanA = Span(2024, 10, 2026, 11), SpanB = Span(2025, 1, 2025, 6) }, sure: true, heuristic: "same-name")));

    [Fact]
    public void NothingToGoOn_Unclear() =>
        Assert.Equal(ClubPairVerdicts.Unclear, Verdict(Pair(ClubPairOverlap.None with
            { SpanA = Span(2024, 10, 2026, 11), SpanB = Span(2025, 1, 2025, 6) })));

    [Fact]
    public void ProtocolsBeatSureHeuristic() =>
        Assert.Equal(ClubPairVerdicts.TwoClubs, Verdict(Pair(new ClubPairOverlap(1, 1, 0, []), sure: true, heuristic: "same-name")));

    // ── Сквозь базу: кандидаты и dry-run склейки ──────────────────────────────

    private sealed class NoStandings : IClubStandingService
    {
        public Task<int> RebuildForCompetitionAsync(int competitionId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RebuildAllAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RebuildForClubAsync(int clubId, CancellationToken ct = default) => Task.FromResult(0);
    }

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Два клуба в двух общих соревнованиях отдельными командами; 3 общих пловца переходили между ними.</summary>
    private static async Task<(Club A, Club B)> SeedTwoClubsAsync(SwimmDbContext db)
    {
        var a = new Club { Name = "הפועל ירושלים" };
        var b = new Club { Name = "הפועל ירושלים אומנותית" };
        var style = new Style { Name = "Freestyle" };
        var c1 = new Competition { Name = "ליגה 1", Date = "07/11/2025", PoolType = "25m" };
        var c2 = new Competition { Name = "ליגה 2", Date = "20/05/2026", PoolType = "25m" };
        var c3 = new Competition { Name = "ליגה 3", Date = "01/09/2026", PoolType = "25m" };
        db.AddRange(a, b, style, c1, c2, c3);
        for (var i = 1; i <= 6; i++)
        {
            var s = new Swimmer { LastName = $"S{i}", FirstName = "X", BirthYear = 2012 };
            db.Swimmers.Add(s);
            // 1–3: плыли за A в c1, за B в c3 (переход); 4–6: в c1 и c2 за свои клубы.
            void R(Club club, Competition comp, DateTime d) => db.Results.Add(new ResultRecord
                { Swimmer = s, Competition = comp, Style = style, Distance = "50", Club = club, Gender = "female", CompetitionDate = d });
            if (i <= 3) { R(a, c1, new(2025, 11, 7)); R(b, c3, new(2026, 9, 1)); }
            else { R(i == 4 ? a : b, c1, new(2025, 11, 7)); R(i == 4 ? a : b, c2, new(2026, 5, 20)); }
        }
        await db.SaveChangesAsync();
        db.ClubCompetitionStandings.AddRange(
            new ClubCompetitionStanding { CompetitionId = c1.Id, ClubId = a.Id },
            new ClubCompetitionStanding { CompetitionId = c1.Id, ClubId = b.Id });
        await db.SaveChangesAsync();
        return (a, b);
    }

    [Fact]
    public async Task DedupCandidates_CarryProtocolsAndRecommendation()
    {
        await using var db = CreateDb(nameof(DedupCandidates_CarryProtocolsAndRecommendation));
        await SeedTwoClubsAsync(db);

        var cand = Assert.Single((await new ClubDedupService(db).FindCandidatesAsync()).Candidates, c => c.Heuristic == "swimmers");

        Assert.NotNull(cand.Protocols);
        Assert.Equal((2, 1, 0), (cand.Protocols!.Meets, cand.Protocols.SeparateStandings, cand.Protocols.SameSwimmerMeets));
        Assert.Equal(["ליגה 2", "ליגה 1"], cand.Protocols.Examples.Select(e => e.Name));
        Assert.Equal(ClubPairVerdicts.TwoClubs, cand.Recommendation!.Verdict);
    }

    [Fact]
    public async Task MergeDryRun_WarnsWhenBothClubsInSameProtocols()
    {
        await using var db = CreateDb(nameof(MergeDryRun_WarnsWhenBothClubsInSameProtocols));
        var (a, b) = await SeedTwoClubsAsync(db);

        var report = await new ClubMergeService(db, new NoStandings()).MergeAsync([new ClubMergePair(a.Id, b.Id)], dryRun: true);

        var pair = Assert.Single(report.Pairs);
        Assert.Equal("dry-run", pair.Status); // предупреждение, не конфликт — решает человек
        var warning = Assert.Single(pair.Actions, x => x.StartsWith('⚠'));
        Assert.Contains("скорее ДВА клуба", warning);
        Assert.Contains("соревнований 2", warning);
    }
}
