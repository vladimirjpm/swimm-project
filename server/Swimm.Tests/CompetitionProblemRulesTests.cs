using System.Collections.Generic;
using System.Linq;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «У старта есть проблема» — значок строки, фильтр «С ошибками» и блок «Что не так»
/// /Admin/Competitions (<see cref="CompetitionProblemRules"/>). По кейсу на причину.
/// </summary>
public class CompetitionProblemRulesTests
{
    private static CompetitionListItemDto Day(int id, int suspects = 0, string? partial = null) =>
        new() { Id = id, Name = "t", Date = "01/11/2026", SuspectCount = suspects, PartialResults = partial };

    private static UnifiedCompetitionRowDto Single(CompetitionListItemDto c) =>
        new() { Stage = CompetitionStage.Imported, Db = new CompetitionRowDto { Single = c } };

    [Fact]
    public void Clean_NoProblem()
    {
        var u = Single(Day(1));
        Assert.False(CompetitionProblemRules.HasProblem(u));
        Assert.Empty(CompetitionProblemRules.Reasons(CompetitionProblemRules.From(u)));
    }

    [Fact]
    public void HealthError_IsProblem()
    {
        var u = Single(Day(1));
        u.Health = [new CompetitionHealthCount("results.merged-sessions", 2, 1)];
        Assert.True(CompetitionProblemRules.HasProblem(u));
        Assert.Equal([CompetitionProblemKind.HealthError], CompetitionProblemRules.Reasons(CompetitionProblemRules.From(u)));
    }

    [Fact]
    public void HealthWarning_IsProblem()
    {
        var u = Single(Day(1));
        u.Health = [new CompetitionHealthCount("results.no-gender", 1, 34)];
        var input = CompetitionProblemRules.From(u);
        Assert.Equal(34, input.HealthWarnings);
        Assert.Equal([CompetitionProblemKind.HealthWarning], CompetitionProblemRules.Reasons(input));
    }

    [Fact]
    public void HealthInfo_IsNotProblem()
    {
        var u = Single(Day(1));
        u.Health = [new CompetitionHealthCount("some.info", 0, 5)];
        Assert.False(CompetitionProblemRules.HasProblem(u));
    }

    [Fact]
    public void Suspect_IsProblem()
    {
        var u = Single(Day(1, suspects: 3));
        Assert.Equal([CompetitionProblemKind.Suspect], CompetitionProblemRules.Reasons(CompetitionProblemRules.From(u)));
    }

    [Fact]
    public void PartialResultsMark_R114_IsIncomplete()
    {
        var u = Single(Day(1, partial: "нет вечерней сессии"));
        Assert.Equal([CompetitionProblemKind.Incomplete], CompetitionProblemRules.Reasons(CompetitionProblemRules.From(u)));
    }

    [Fact]
    public void ShortOfSiteDays_R115_IsIncomplete()
    {
        var u = new UnifiedCompetitionRowDto
        {
            Stage = CompetitionStage.Imported,
            Db = new CompetitionRowDto { EventId = 7, DayCount = 2, Days = [Day(1), Day(2)] },
            Status = new CompetitionRowStatus(CompetitionRowState.WaitingProtocol, WaitingProtocolReason.Incomplete)
                with { PulledDays = 2, SiteDays = 3 },
        };
        Assert.Equal([CompetitionProblemKind.Incomplete], CompetitionProblemRules.Reasons(CompetitionProblemRules.From(u)));
    }

    [Fact]
    public void SplitsNeedRetry_IsProblem()
    {
        var u = Single(Day(1));
        u.SplitsNeedRetry = true;
        Assert.Equal([CompetitionProblemKind.SplitsRetry], CompetitionProblemRules.Reasons(CompetitionProblemRules.From(u)));
    }

    [Fact]
    public void Event_SumsDays_SuspectsAndPartialDays()
    {
        var u = new UnifiedCompetitionRowDto
        {
            Stage = CompetitionStage.Imported,
            Db = new CompetitionRowDto { EventId = 7, DayCount = 2, SuspectCount = 5, PartialDayCount = 1, Days = [Day(1), Day(2)] },
        };
        var input = CompetitionProblemRules.From(u);
        Assert.Equal(5, input.SuspectCount);
        Assert.True(input.Incomplete);
    }

    [Fact]
    public void SiteOnlyRow_NoDb_NoProblem() =>
        Assert.False(CompetitionProblemRules.HasProblem(new UnifiedCompetitionRowDto { Stage = CompetitionStage.OnSite }));

    [Fact]
    public void Reasons_InImportanceOrder()
    {
        var all = new CompetitionProblemInput(1, 1, 1, true, true);
        Assert.Equal(
            [CompetitionProblemKind.HealthError, CompetitionProblemKind.HealthWarning, CompetitionProblemKind.Suspect,
             CompetitionProblemKind.Incomplete, CompetitionProblemKind.SplitsRetry],
            CompetitionProblemRules.Reasons(all));
    }

    [Fact]
    public void Merge_SumsSameCheckAcrossDays_ErrorsFirst()
    {
        var merged = CompetitionProblemRules.Merge(new List<CompetitionHealthCount>
        {
            new("results.no-gender", 1, 4), new("results.no-gender", 1, 6),
            new("results.merged-sessions", 2, 1), new("x.info", 0, 9),
        });
        Assert.Equal(2, merged.Count);
        Assert.Equal(new CompetitionHealthCount("results.merged-sessions", 2, 1), merged[0]);
        Assert.Equal(new CompetitionHealthCount("results.no-gender", 1, 10), merged[1]);
    }

    [Fact]
    public void Hint_UsesTitles_FallsBackToId()
    {
        var counts = new[] { new CompetitionHealthCount("a.one", 1, 2), new CompetitionHealthCount("b.two", 1, 3), new CompetitionHealthCount("c.err", 2, 1) };
        var hint = CompetitionProblemRules.Hint(counts, 1, new Dictionary<string, string> { ["a.one"] = "Первая" });
        Assert.Equal("Первая — 2\nb.two — 3", hint);
        Assert.Equal("c.err — 1", CompetitionProblemRules.Hint(counts, 2, null));
    }
}
