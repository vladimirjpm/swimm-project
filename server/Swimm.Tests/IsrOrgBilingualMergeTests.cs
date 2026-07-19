using System;
using System.Collections.Generic;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регресс на склейку EN+HE пары протоколов (MergeBilingual): loglig иногда теряет
/// строку в одном из языковых рендеров (реальный кейс — зимняя «ארנה 8-11», где
/// безвременной результат есть в HE и отсутствует в EN). Merge ресинкается по
/// (Heat, Lane); осиротевшая строка идёт одноязычной, а НЕ сдвигает всю склейку.
/// </summary>
public class IsrOrgBilingualMergeTests
{
    private static IsrOrgResult R(int heat, int lane, string last, string? time, int year = 2017) => new(
        Country: "IL", Position: time == null ? "-" : "1", Heat: heat, Lane: lane,
        LastName: last, FirstName: "X", BirthYear: year, Club: "Club",
        Time: time, TimeFailNote: time == null ? "DQ" : null, InternationalPoints: 0,
        IsRelay: false, RelayTeamName: null, RelaySwimmersName: null, RelaySwimmers: null);

    private static IsrOrgCompetitionResult Comp(string lang, params IsrOrgResult[] results) => new(
        Competition: $"comp-{lang}", AgeGroup: "9", Date: "19/02/2026",
        Event: "50m Breaststroke - Boys 9", EventStyleName: "breaststroke",
        EventStyleLen: "50", EventStyleGender: "male", EventStyleAge: "9",
        PoolType: "25m", Results: results.ToList());

    [Fact]
    public void RowMissingInEn_ResyncsByHeatLane_HebrewOnlyRowKeepsHebrewNames()
    {
        // HE: 4 строки; EN: 3 (потерян зэйдмן heat=2 lane=7). Раньше пары сдвигались
        // и MOISEEV склеивался с זיידמן; теперь — ресинк, имена не перепутаны.
        var en = Comp("en", R(2, 1, "GOAZ", null), R(2, 3, "ZELINGER", null), R(2, 2, "MOISEEV", null));
        var he = Comp("he", R(2, 1, "גואז", null), R(2, 3, "זלינגר", null), R(2, 7, "זיידמן", null), R(2, 2, "מויסייב", null));

        var merged = IsrOrgParser.MergeBilingual([en], [he], "IL", false, false, null).ToList();

        Assert.Equal(4, merged.Count);
        Assert.Equal(("זיידמן", "זיידמן"), (merged[2].LastName, merged[2].LastNameEn)); // одноязычная — EN-фоллбек
        Assert.Equal(("מויסייב", "MOISEEV"), (merged[3].LastName, merged[3].LastNameEn));
    }

    [Fact]
    public void RowMissingInHe_TailAndMiddle_EmittedFromEnSide()
    {
        var en = Comp("en", R(1, 1, "A", "00:30.00"), R(1, 2, "B", "00:31.00"), R(1, 3, "C", "00:32.00"));
        var he = Comp("he", R(1, 1, "א", "00:30.00"), R(1, 3, "ג", "00:32.00"));

        var merged = IsrOrgParser.MergeBilingual([en], [he], "IL", false, false, null).ToList();

        Assert.Equal(3, merged.Count);
        Assert.Equal(("B", "B"), (merged[1].LastName, merged[1].LastNameEn)); // EN-only строка
        Assert.Equal(("ג", "C"), (merged[2].LastName, merged[2].LastNameEn));
    }

    [Fact]
    public void SameSlotDifferentTime_StillThrows()
    {
        var en = Comp("en", R(1, 1, "A", "00:30.00"));
        var he = Comp("he", R(1, 1, "א", "00:31.00"));

        Assert.Throws<InvalidOperationException>(() =>
            IsrOrgParser.MergeBilingual([en], [he], "IL", false, false, null).ToList());
    }

    [Fact]
    public void TrueDivergence_NoResyncFound_Throws()
    {
        var en = Comp("en", R(1, 1, "A", "00:30.00"), R(9, 9, "Z", "00:59.00"));
        var he = Comp("he", R(2, 2, "ב", "00:40.00"), R(8, 8, "ח", "00:58.00"));

        Assert.Throws<InvalidOperationException>(() =>
            IsrOrgParser.MergeBilingual([en], [he], "IL", false, false, null).ToList());
    }
}
