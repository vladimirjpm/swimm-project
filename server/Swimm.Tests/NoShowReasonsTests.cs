using Swimm.Infrastructure.Services.DataChecks;
using Xunit;
using static Swimm.Infrastructure.Services.DataChecks.NoShowReasons;

namespace Swimm.Tests;

/// <summary>
/// Причина на каждую заявку без результата (<c>entries.no-show-unmatched</c>; просьба Влада
/// 25.11.2026). Случаи — из разбора 18 живых заявок compID 16899, 16903, 16911, 16924, 16926.
/// </summary>
public class NoShowReasonsTests
{
    private static Person P(int id, string last, string first, int year, int? loglig = null, int? club = 1) =>
        new(id, last, first, year, loglig, club);

    private static Entry E(Person p, string gender = "male", bool relay = false, int heat = 2, int lane = 2, int club = 1) =>
        new(p, club, gender, relay, heat, lane);

    private static LaneRow Row(Person p, string gender = "male", string? time = "00:37.35", bool ownEntry = false,
        int club = 1, params Person[] relay) =>
        new(p, club, gender, time, ownEntry, relay);

    // 16899: נבו בן — у карточки с loglig-id год 2008, у второй (по протоколу) 2018.
    [Fact]
    public void Twin_SameNameInHisLane_EvenWithDifferentYear()
    {
        var entry = E(P(65109, "נבו", "בן", 2008, loglig: 333527));
        var r = Classify(entry, [Row(P(64447, "סרדניצקי", "עומר", 2017), gender: "female"),
            Row(P(65165, "נבו", "בן", 2018))], []);

        Assert.Equal(Kind.Twin, r.Kind);
        Assert.Contains("#65165", r.Text);
        Assert.Contains("год расходится: у заявки 2008, у второй карточки 2018", r.Text);
    }

    // 16899: «ליאונג / אלעד תום» и «ליאונג אלעד / תום» — те же токены, разбиты иначе;
    // 16903: «ליבמן מילי» ⊂ «ליבמן מילי אריאל»; 16924: опечатка נצ׳ייבקסי / נצ׳ייבסקי.
    [Theory]
    [InlineData("ליאונג", "אלעד תום", "ליאונג אלעד", "תום")]
    [InlineData("ליבמן", "מילי אריאל", "ליבמן", "מילי")]
    [InlineData("נצ׳ייבקסי", "אורי", "נצ׳ייבסקי", "אורי")]
    public void SameName_ToleratesSplitShortenedAndTypo(string l1, string f1, string l2, string f2) =>
        Assert.True(SameName(P(1, l1, f1, 2016), P(2, l2, f2, 2016)));

    [Fact]
    public void SameName_DifferentPeople_AreNot() =>
        Assert.False(SameName(P(1, "ליבמן", "מילי", 2016), P(2, "ליבמן", "יולי", 2016)));

    // 16926: אדרי איתי #61243 (клуб 518) и #6663 (клуб 514) — у обоих свои loglig-id: два человека.
    [Fact]
    public void Namesake_BothHaveOwnLogligIds_MoveTheRow_NotMerge()
    {
        var entry = E(P(61243, "אדרי", "איתי", 2015, loglig: 276096, club: 518), club: 518);
        var r = Classify(entry, [Row(P(6663, "אדרי", "איתי", 2015, loglig: 274690, club: 514), time: "", club: 518)], []);

        Assert.Equal(Kind.Namesake, r.Kind);
        Assert.Contains("перенести строку", r.Text);
        Assert.Contains("клуб строки — его", r.Text);
    }

    // 16903: команда плыла другим составом.
    [Fact]
    public void Relay_TeamSwamWithoutHim_IsSubstitution()
    {
        var entry = E(P(5877, "רגב", "פלג", 2016), relay: true);
        var r = Classify(entry, [Row(P(6047, "סגרון", "נועם", 2016), relay:
            [P(6047, "סגרון", "נועם", 2016), P(64310, "א", "ב", 2016), P(6053, "ג", "ד", 2016), P(5967, "ה", "ו", 2016)])], []);

        Assert.Equal(Kind.RelaySubstitution, r.Kind);
        Assert.Contains("סגרון נועם", r.Text);
    }

    [Fact]
    public void Relay_NoTeamInLane_IsEmpty() =>
        Assert.Equal(Kind.Empty, Classify(E(P(1, "א", "ב", 2016), relay: true), [], []).Kind);

    // 16911: פולוזוב וסיליסה — в её дорожке другая пловчиха того же клуба без своей заявки.
    [Fact]
    public void LaneTakenBySomeoneWithoutEntry_SameClub()
    {
        var entry = E(P(64416, "פולוזוב", "וסיליסה", 2017, club: 486), gender: "female", club: 486);
        var r = Classify(entry, [Row(P(65167, "גלסברג", "נועה", 2017), gender: "female", time: "00:42.69", club: 486),
            Row(P(65099, "גוסצנקו", "נתן", 2017), gender: "male", ownEntry: true, club: 486)], []);

        Assert.Equal(Kind.LaneTakenNoEntry, r.Kind);
        Assert.Contains("#65167", r.Text);
        Assert.Contains("тот же клуб", r.Text);
    }

    [Fact]
    public void LaneTakenBySomeoneWithEntry_IsSeedChange() =>
        Assert.Equal(Kind.LaneTakenSeed,
            Classify(E(P(1, "א", "ב", 2016)), [Row(P(2, "ג", "ד", 2016), ownEntry: true)], []).Kind);

    // 16911: שניר שי — в её (женском) заплыве и дорожке никого; мальчик в той же нумерации не в счёт.
    [Fact]
    public void EmptyLaneOfHerGender_IsHonestNoShow()
    {
        var entry = E(P(6346, "שניר", "שי", 2016), gender: "female", heat: 3, lane: 3);
        var r = Classify(entry, [Row(P(6297, "ליטבין", "גבריאל", 2016), gender: "male", ownEntry: true)], []);

        Assert.Equal(Kind.Empty, r.Kind);
        Assert.Contains("честная неявка", r.Text);
    }

    [Fact]
    public void OwnRowInAnotherHeat_SwamElsewhere()
    {
        var r = Classify(E(P(1, "א", "ב", 2016)), [], [new OwnRow(4, 6, "00:40.00")]);
        Assert.Equal(Kind.SwamElsewhere, r.Kind);
        Assert.Contains("заплыв 4, дорожка 6 (00:40.00)", r.Text);
    }

    [Fact]
    public void Details_SummaryFirst_LinesAfter_CutAtColumnLimit()
    {
        var reasons = Enumerable.Range(1, 80)
            .Select(i => ($"#{i} {new string('א', 40)}: дорожка пуста", Kind.Empty)).ToList();
        reasons.Insert(0, ("#0 двойник", Kind.Twin));

        var text = NoShowUnmatchedCheck.Details(16903, reasons);

        Assert.StartsWith("compID 16903 · двойник пловца — 1, дорожка пуста — честная неявка — 80\n#0 двойник", text);
        Assert.True(text.Length <= 4000);
        Assert.Matches(@"\n… ещё \d+$", text);
    }
}
