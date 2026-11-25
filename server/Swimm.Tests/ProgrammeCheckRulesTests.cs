using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
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
        Assert.Equal("Оба дня у нас есть, но записаны одной датой.", v.Headline);
        Assert.Contains("заплывы 28.03 и 29.03 разойдутся по своим дням", v.Hint);
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
        Assert.Contains("есть 28.03, а в базе у старта этого дня нет", v.Headline);
        Assert.Contains("остальные дни лежат не здесь", v.Headline);
        // Не «Перезатянуть» — перезалив дал бы дубль.
        Assert.Contains("дни есть, события нет", v.Hint);
        Assert.Contains("получится дубль", v.Hint);
    }

    [Fact]
    public void Collapsed_PlanDateOutsideFederation_NoButton()
    {
        DateOnly d28 = D(2025, 3, 28), d29 = D(2025, 3, 29), d30 = D(2025, 3, 30);
        var plan = Plan(new(d29, 1, 1665, true, 1618, 0), new(d30, 2, null, false, 1072, 0));

        var v = ProgrammeCheckRules.Verdict(new(d28, d29, [d29], 12160, [d29, d30], SplitPlan: plan));

        Assert.Equal(ProgrammeVerdictKind.Collapsed, v.Kind);
        Assert.False(v.CanSplit);
        Assert.Null(v.PlanFingerprint);
        Assert.Contains("дата программы 30.03 вне дат федерации (28–29.03)", v.SplitBlocked);
    }

    [Fact]
    public void Collapsed_FederationHasNoMissingDay_NoButton()
    {
        DateOnly d29 = D(2025, 3, 29), d30 = D(2025, 3, 30);
        var plan = Plan(new(d29, 1, 1665, true, 1618, 0), new(d30, 2, null, false, 1072, 0));

        // Однодневный старт по федерации (29.03), а в сетке loglig «лишняя» дата — Р115 нет.
        var v = ProgrammeCheckRules.Verdict(new(d29, d29, [d29], 12160, [d29, d30], SplitPlan: plan));

        Assert.False(v.CanSplit);
        Assert.NotNull(v.SplitBlocked);
    }

    [Fact]
    public void Collapsed_Fingerprint_DatesAndRows()
    {
        DateOnly d28 = D(2025, 3, 28), d29 = D(2025, 3, 29);
        var plan = Plan(new(d29, 2, 1665, true, 1618, 3), new(d28, 1, null, false, 1072, 0));

        var v = ProgrammeCheckRules.Verdict(new(d28, d29, [d29], 12160, [d28, d29], SplitPlan: plan));

        Assert.Equal("2025-03-28=1072;2025-03-29=1618", v.PlanFingerprint);
    }

    // ── (б+в): в привязанной программе только дни из базы — решает поиск соседей ──

    private static LogligCompetitionSummaryDto Summary(int id, string title, params DateOnly[] dates) => new(id, title, dates);

    private const string Millennium6 = "תחרות חלופית ל&quot;מילניום&quot; יום שישי 6.12.24 - 06/12/2024";

    [Fact]
    public void Maccabiah_16723_NoCandidates_AcceptPrimary()
    {
        DateOnly d5 = D(2026, 7, 5), d6 = D(2026, 7, 6), d7 = D(2026, 7, 7), d10 = D(2026, 7, 10);
        Assert.True(ProgrammeCheckRules.NeedsNeighbourSearch(d5, d10, [d5, d6, d7], [d5, d6, d7]));

        var v = ProgrammeCheckRules.Verdict(new(d5, d10, [d5, d6, d7], 12327, [d5, d6, d7],
            BoundTitle: "מכביה 2026 - שחייה - 05/07/2026",
            Neighbours: [Summary(12325, "ליגת מאסטרס מחוז מרכז - 04/07/2026", D(2026, 7, 4)), Summary(12329, "גביע הנוער - 09/07/2026", D(2026, 7, 9))]));

        Assert.Equal(ProgrammeVerdictKind.OnlyKnownDays, v.Kind);
        Assert.True(v.AcceptPrimary);
        Assert.Null(v.AcceptWarning);
        Assert.Empty(v.Candidates!);
        Assert.False(v.CanSplit);
        Assert.Equal(MissingDayTexts.NothingToPull, v.Headline);
        Assert.Contains("Нажмите «Принять как есть»", v.Headline);
    }

    [Fact]
    public void Comp6611_Candidate11590_And11589RejectedByDate()
    {
        DateOnly fri = D(2024, 12, 6), sat = D(2024, 12, 7);

        var v = ProgrammeCheckRules.Verdict(new(fri, sat, [fri], 11588, [fri],
            BoundTitle: Millennium6,
            Neighbours:
            [
                Summary(11589, "\tתחרות חלופית ל&quot;מילניום&quot; יום שבת 7.12.24 - 24/11/2024"),
                Summary(11590, "תחרות חלופית ל\"מילניום\" יום שבת 7.12.24 - 07/12/2024", sat),
                Summary(11591, "אליפות מחוז צפון - 08/12/2024", D(2024, 12, 8)),
            ]));

        Assert.Equal(ProgrammeVerdictKind.OnlyKnownDays, v.Kind);
        Assert.False(v.AcceptPrimary);
        Assert.NotNull(v.AcceptWarning);
        Assert.Equal([11590], v.Candidates!.Select(c => c.LogligId));
        Assert.Equal([11589], v.Similar!.Select(c => c.LogligId));
        // Один кандидат — простыми словами и кнопка (Влад 25.11.2026); черновик 11589 в текст не идёт.
        Assert.Equal("Второй день есть у федерации, но у нас его нет", v.Headline);
        Assert.Contains("Суббота 07.12 нашлась отдельно", v.Hint);
        Assert.Equal("Затянуть субботу 07.12", v.PullLabel);
        Assert.Contains("Не нажимайте, пока не затянули субботу", v.AcceptWarning);
        Assert.Empty(v.Lines);
    }

    [Fact]
    public void Comp488_Candidate9817()
    {
        DateOnly fri = D(2023, 12, 22), sat = D(2023, 12, 23);

        var v = ProgrammeCheckRules.Verdict(new(fri, sat, [fri], 9816, [fri],
            BoundTitle: "הזדמנות אחרונה 2023 יום שישי - 22/12/2023",
            Neighbours: [Summary(9817, "הזדמנות אחרונה 2023 יום שבת - 23/12/2023", sat)]));

        Assert.Equal(ProgrammeVerdictKind.OnlyKnownDays, v.Kind);
        Assert.Equal(9817, Assert.Single(v.Candidates!).LogligId);
        Assert.False(v.AcceptPrimary);
        Assert.Equal("Затянуть субботу 23.12", v.PullLabel);
        Assert.Contains("У нас в базе только пятница 22.12", v.Hint);
    }

    [Fact]
    public void NeighboursNotChecked_AcceptNotPrimary()
    {
        DateOnly fri = D(2024, 12, 6), sat = D(2024, 12, 7);

        var noSearch = ProgrammeCheckRules.Verdict(new(fri, sat, [fri], 11588, [fri]));
        var someFailed = ProgrammeCheckRules.Verdict(new(fri, sat, [fri], 11588, [fri],
            BoundTitle: Millennium6, Neighbours: [], NeighboursFailed: 2));

        Assert.False(noSearch.AcceptPrimary);
        Assert.Contains("проверить не удалось", noSearch.Hint);
        Assert.False(someFailed.AcceptPrimary);
        Assert.Contains("2 из 6 соседних номеров loglig не ответили", someFailed.Hint);
    }

    [Fact]
    public void NeighbourIds_PlusMinusThree_NotSelf_Positive()
    {
        Assert.Equal([11585, 11586, 11587, 11589, 11590, 11591], ProgrammeCheckRules.NeighbourIds(11588));
        Assert.Equal([1, 3, 4, 5], ProgrammeCheckRules.NeighbourIds(2));
    }

    [Theory]
    [InlineData("תחרות חלופית ל&quot;מילניום&quot; יום שישי 6.12.24 - 06/12/2024", "תחרות חלופית למילניום")]
    [InlineData("\tתחרות חלופית ל\"מילניום\" יום שבת 7.12.24 - 24/11/2024", "תחרות חלופית למילניום")]
    [InlineData("הזדמנות אחרונה 2023 יום ו' - 22/12/2023", "הזדמנות אחרונה 2023")]
    [InlineData("גביע  ״השרון״ שבת", "גביע השרון")]
    [InlineData("ליגה סבב שני - 01/02/2025", "ליגה סבב שני")]
    [InlineData("אליפות המחוז בשבת 7.12.24", "אליפות המחוז")]
    [InlineData("אליפות המחוז ביום שישי - 06/12/2024", "אליפות המחוז")]
    [InlineData("אליפות המחוז וביום שבת", "אליפות המחוז")]
    public void Normalize_DropsWeekdayDatesQuotes(string title, string expected) =>
        Assert.Equal(expected, LogligTitleRules.Normalize(title));

    [Fact]
    public void TitleDate_FromTail()
    {
        Assert.Equal(D(2024, 11, 24), LogligTitleRules.TitleDate("תחרות חלופית ל&quot;מילניום&quot; יום שבת 7.12.24 - 24/11/2024"));
        Assert.Null(LogligTitleRules.TitleDate("בלי תאריך"));
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
