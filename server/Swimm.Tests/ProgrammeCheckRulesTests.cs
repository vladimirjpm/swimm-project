using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р123: вердикт «Проверить, чего не хватает» у Р115. Кейсы — три живых случая 24.11.2026:
/// 6602 (дни склеены), 488 (день отдельным соревнованием loglig), 16723 Маккабиада (даты Игр шире).
/// </summary>
public class ProgrammeCheckRulesTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static CompetitionDaySplitReport Plan(params CompetitionDaySplitDay[] days) =>
        new(1665, "אליפות עמק חפר", 12160, days, new Dictionary<string, int>(), null, false, null);

    [Fact]
    public void Collapsed_6602_PlanFromDryRun_ButtonOnExistingCompetition()
    {
        DateOnly d28 = D(2025, 3, 28), d29 = D(2025, 3, 29);
        var plan = Plan(new(d28, 1, null, false, 1072, 0), new(d29, 2, 1665, true, 1618, 3));

        var v = ProgrammeCheckRules.Verdict(new(d28, d29, [d29], 12160, [d28, d29], SplitPlan: plan));

        Assert.Equal(ProgrammeVerdictKind.Collapsed, v.Kind);
        Assert.True(v.CanSplit);
        Assert.Equal(1665, v.SplitCompetitionId);
        Assert.False(v.AcceptPrimary);
        Assert.Equal(["день 1 28.03: 1072 строки — новое соревнование", "день 2 29.03: 1618 строк (медиа 3) — остаётся #1665"], v.Lines);
        Assert.Contains("Строки 28.03 — в нём", v.Headline);
    }

    [Fact]
    public void NeedsSplitPlan_OnlyWhenOneDbDayAndProgrammeHasMore()
    {
        DateOnly d28 = D(2025, 3, 28), d29 = D(2025, 3, 29);
        Assert.True(ProgrammeCheckRules.NeedsSplitPlan([d29], [d28, d29]));
        Assert.False(ProgrammeCheckRules.NeedsSplitPlan([d29], [d29]));
        Assert.False(ProgrammeCheckRules.NeedsSplitPlan([d28, d29], [d28, d29, D(2025, 3, 30)]));
        Assert.False(ProgrammeCheckRules.NeedsSplitPlan([d29], null));
    }

    [Fact]
    public void Collapsed_WithAmbiguousRows_NoButton_ShowsReason()
    {
        DateOnly d28 = D(2025, 3, 28), d29 = D(2025, 3, 29);
        var plan = new CompetitionDaySplitReport(1665, "x", 12160, [],
            new Dictionary<string, int> { ["freestyle 100 male"] = 40 },
            "у 40 строк нет однозначного дня — всё или ничего", false, null);

        var v = ProgrammeCheckRules.Verdict(new(d28, d29, [d29], 12160, [d28, d29], SplitPlan: plan));

        Assert.Equal(ProgrammeVerdictKind.Collapsed, v.Kind);
        Assert.False(v.CanSplit);
        Assert.Contains("нет однозначного дня", v.SplitBlocked);
        Assert.Equal(["без однозначного дня: freestyle 100 male ×40"], v.Lines);
    }

    [Fact]
    public void ProgrammeDayMissing_RowsNotHere_DayNotPulled()
    {
        DateOnly d28 = D(2025, 3, 28), d29 = D(2025, 3, 29);
        var plan = new CompetitionDaySplitReport(1665, "x", 12160, [], new Dictionary<string, int>(),
            "все строки — дисциплины одного дня (29/03/2025) из 2 по источнику — остальные дни лежат не здесь", false, null);

        var v = ProgrammeCheckRules.Verdict(new(d28, d29, [d29], 12160, [d28, d29], SplitPlan: plan));

        Assert.Equal(ProgrammeVerdictKind.DayNotPulled, v.Kind);
        Assert.False(v.CanSplit);
        Assert.Contains("есть 28.03, а в базе этого дня нет", v.Headline);
        Assert.Contains("остальные дни лежат не здесь", v.Headline);
    }

    [Fact]
    public void Maccabiah_16723_ProgrammeNarrowerThanFederation_AcceptPrimary()
    {
        DateOnly d5 = D(2026, 7, 5), d6 = D(2026, 7, 6), d7 = D(2026, 7, 7), d10 = D(2026, 7, 10);

        var v = ProgrammeCheckRules.Verdict(new(d5, d10, [d5, d6, d7], 12327, [d5, d6, d7]));

        Assert.Equal(ProgrammeVerdictKind.ProgrammeNarrower, v.Kind);
        Assert.True(v.AcceptPrimary);
        Assert.False(v.CanSplit);
        Assert.StartsWith("Программа на loglig — только 05–07.07, все эти дни в базе. Даты федерации шире (например, даты всех Игр)", v.Headline);
        Assert.Contains("08.07, 09.07, 10.07 в программе нет", v.Headline);
    }

    [Fact]
    public void Comp488_OneDayProgramme_DayElsewhere_HintWithoutButton()
    {
        DateOnly fri = D(2023, 12, 22), sat = D(2023, 12, 23);

        var v = ProgrammeCheckRules.Verdict(new(fri, sat, [fri], 9816, [fri]));

        Assert.Equal(ProgrammeVerdictKind.DayElsewhere, v.Kind);
        Assert.False(v.CanSplit);
        Assert.False(v.AcceptPrimary);
        Assert.Contains("23.12 нет ни в базе, ни в программе", v.Headline);
        Assert.Contains("compID 488 — суббота в loglig 9817 при привязанном 9816", v.Hint);
        Assert.Contains("docs/pull-to-prod.md", v.Hint);
    }

    [Theory]
    [InlineData(null, null, false, "нет loglig-id")]
    [InlineData(9816, "timeout", false, "loglig не ответил")]
    [InlineData(9816, null, true, "пуста")]
    public void NoVerdict_HonestText(int? loglig, string? error, bool emptyProgramme, string expected)
    {
        DateOnly fri = D(2023, 12, 22), sat = D(2023, 12, 23);
        IReadOnlyList<DateOnly>? programme = error != null || loglig == null ? null : emptyProgramme ? [] : [fri];

        var v = ProgrammeCheckRules.Verdict(new(fri, sat, [fri], loglig, programme, error));

        Assert.Equal(ProgrammeVerdictKind.None, v.Kind);
        Assert.False(v.CanSplit);
        Assert.False(v.AcceptPrimary);
        Assert.Contains(expected, v.Headline);
    }
}
