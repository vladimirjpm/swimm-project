using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>Р124: правила кнопки «Затянуть этот день» — живые случаи 6611 (11588 → 11590) и 488 (9816 → 9817).</summary>
public class SiblingDayRulesTests
{
    private static readonly DateOnly Fri = new(2024, 12, 6), Sat = new(2024, 12, 7);
    private const string Bound = "תחרות חלופית ל\"מילניום\" יום שישי 6.12.24 - 06/12/2024";
    private const string SatTitle = "תחרות חלופית ל\"מילניום\" יום שבת 7.12.24 - 07/12/2024";

    private static LogligCompetitionSummaryDto Sib(string title = SatTitle, params DateOnly[] dates) =>
        new(11590, title, dates.Length > 0 ? dates : [Sat]);

    private static string? Candidate(LogligCompetitionSummaryDto? sib, int id = 11590, string? bound = Bound, IReadOnlyList<DateOnly>? db = null) =>
        SiblingDayRules.CandidateRefusal(11588, bound, id, sib, Fri, Sat, db ?? [Fri]);

    [Fact]
    public void Candidate_6611Saturday_Fits() => Assert.Null(Candidate(Sib()));

    [Fact]
    public void Candidate_DateFromTitle_WhenNoDisciplineDates_Fits() =>
        Assert.Null(Candidate(new LogligCompetitionSummaryDto(11590, SatTitle, [])));

    [Theory]
    [InlineData(11588, "то же соревнование")]
    [InlineData(11595, "не рядом с нашим")]
    public void Candidate_WrongNumber_Refused(int id, string why) =>
        Assert.Contains(why, Candidate(Sib(), id));

    [Fact]
    public void Candidate_NotOnLoglig_Refused() => Assert.Contains("такого соревнования нет", Candidate(null));

    [Fact]
    public void Candidate_OtherTitle_Refused() =>
        Assert.Contains("другое соревнование", Candidate(Sib("אליפות חורף 2024 - 07/12/2024")));

    [Fact]
    public void Candidate_DraftWithOtherDate_Refused() =>
        // 11589: то же название, дата 24/11/2024 — «похожее», не кандидат.
        Assert.Contains("не тот день", Candidate(Sib(dates: new DateOnly(2024, 11, 24))));

    [Fact]
    public void Candidate_DayAlreadyInDb_Refused() =>
        Assert.Contains("затягивать нечего", Candidate(Sib(), db: [Fri, Sat]));

    [Fact]
    public void Candidate_TwoDates_Refused() =>
        Assert.Contains("несколько дат", SiblingDayRules.CandidateRefusal(11588, Bound, 11590, Sib(dates: [Sat, Sat.AddDays(1)]),
            Fri, Sat.AddDays(1), [Fri]));

    [Fact]
    public void Candidate_BoundTitleUnknown_Refused() => Assert.Contains("не удалось прочитать", Candidate(Sib(), bound: null));

    [Fact]
    public void FileDate_OneMissingDay_Fits() =>
        Assert.Null(SiblingDayRules.FileDateRefusal(["07/12/2024", "07/12/2024"], [Sat], [Sat], [Fri]));

    [Theory]
    [InlineData("06/12/2024", "у нас уже есть")]
    [InlineData("08/12/2024", "не тот день")]
    [InlineData("7.12.24", "непонятная дата")]
    public void FileDate_Wrong_Refused(string date, string why) =>
        Assert.Contains(why, SiblingDayRules.FileDateRefusal([date], [Sat], [Sat], [Fri]));

    [Fact]
    public void FileDate_TwoDates_Refused() =>
        Assert.Contains("2 даты", SiblingDayRules.FileDateRefusal(["06/12/2024", "07/12/2024"], [Sat], [Sat], []));

    [Fact]
    public void FileDate_OtherThanLogligDate_Refused() =>
        Assert.Contains("а у соревнования на loglig", SiblingDayRules.FileDateRefusal(["07/12/2024"], [Fri.AddDays(-1)],
            [Sat, Fri.AddDays(-1)], [Fri]));

    private static SiblingSwimKey K(string person, int heat, int lane, string style = "freestyle") =>
        new(person, 2010, style, "50", heat, lane);

    [Fact]
    public void Overlap_SameSwimSamePerson_Counted_OtherStyleOrNoLane_Not()
    {
        var db = new[] { K("לוי|נועה", 3, 4), K("כהן|דנה", 2, 5), K("לוי|נועה", 0, 0) };
        var file = new[] { K("לוי|נועה", 3, 4), K("כהן|דנה", 2, 5, "backstroke"), K("לוי|נועה", 0, 0) };

        var o = SiblingDayRules.Overlap(file, db);

        Assert.Equal([K("לוי|נועה", 3, 4)], o);
    }

    [Fact]
    public void OverlapRefusal_FewCoincidences_Pass_SameProtocol_Refused()
    {
        var two = Enumerable.Range(1, 2).Select(i => K("p" + i, 1, i)).ToList();
        var many = Enumerable.Range(1, 60).Select(i => K("p" + i, i, 1)).ToList();

        Assert.Null(SiblingDayRules.OverlapRefusal(two, 1000));        // 2 < 3
        Assert.Null(SiblingDayRules.OverlapRefusal(many.Take(40).ToList(), 1000)); // 40 < 5% от 1000
        Assert.Contains("тот же протокол", SiblingDayRules.OverlapRefusal(many, 1000));
        Assert.Contains("тот же протокол", SiblingDayRules.OverlapRefusal(many.Take(3).ToList(), 20));
    }

    [Fact]
    public void Person_IgnoresSpacesAndPunctuation() =>
        Assert.Equal(SiblingDayRules.Person("בן-בסט", "נועה"), SiblingDayRules.Person("בן בסט", " נועה"));

    [Theory]
    [InlineData("25m", null, "25m", false)]
    [InlineData("25m", "25m", "25m", false)]
    [InlineData(null, "50m", "50m", false)]
    [InlineData("25", "50m", "25m", true)]
    [InlineData(null, null, null, false)]
    public void DefaultPool_FromExistingDay_ConflictWithRegulation_NoDefault(string? existing, string? regulation,
        string? pool, bool conflict) =>
        Assert.Equal((pool, conflict), SiblingDayRules.DefaultPool(existing, regulation));

    [Theory]
    [InlineData("תחרות חלופית ל\"מילניום\" יום שישי 6.12.24", "תחרות חלופית ל\"מילניום\"")]
    [InlineData("תחרות חלופית ל\"מילניום\" יום שבת 7.12.24 - 07/12/2024", "תחרות חלופית ל\"מילניום\"")]
    [InlineData("הזדמנות אחרונה 2023", "הזדמנות אחרונה 2023")]
    public void CommonName_DropsWeekdayAndDates_KeepsQuotes(string title, string expected) =>
        Assert.Equal(expected, LogligTitleRules.CommonName(title));

    [Fact]
    public void PullCandidate_OnlyForSingleCandidate()
    {
        ProgrammeCheckVerdict V(ProgrammeVerdictKind k, params ProgrammeNeighbour[] c) =>
            new(k, "h", [], null, null, null, false, Candidates: c);
        var a = new ProgrammeNeighbour(11590, SatTitle, [Sat]);
        var b = new ProgrammeNeighbour(11591, SatTitle, [Sat]);

        Assert.Equal(11590, ProgrammeCheckRules.PullCandidate(V(ProgrammeVerdictKind.OnlyKnownDays, a)));
        Assert.Null(ProgrammeCheckRules.PullCandidate(V(ProgrammeVerdictKind.OnlyKnownDays, a, b)));
        Assert.Null(ProgrammeCheckRules.PullCandidate(V(ProgrammeVerdictKind.OnlyKnownDays)));
        Assert.Null(ProgrammeCheckRules.PullCandidate(V(ProgrammeVerdictKind.Collapsed, a)));
    }
}
