using System.Collections.Generic;
using System.Linq;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сверка личных мест с пособытийным источником loglig (И-62, 16891): PDF сводит секцию
/// «כללי» обоих полов в один список со сквозными местами, страница события — одного пола.
/// </summary>
public class LogligPlaceMatcherTests
{
    private const string Day = "06/11/2026";

    private static PlaceRowFromSource Src(string gender, int heat, int lane, int? timeMs, int? position,
        string category = "כללי", int birthYear = 2015) =>
        new(Day, "backstroke", "200", gender, category, heat, lane, "name", birthYear, timeMs, position);

    private static PlaceRowInDb Db(long id, string gender, int heat, int lane, int? timeMs, int? position,
        int birthYear = 2015) =>
        new(id, Day, "backstroke", "200", gender, heat, lane, "name", birthYear, timeMs, position);

    /// <summary>200 на спине: у нас места из общего списка (4, 5, 8), у источника девочки — 1, 2, 3.</summary>
    [Fact]
    public void OpenSection_MergedPdfPlaces_BecomePerGenderPlaces()
    {
        var source = new[]
        {
            Src("female", 3, 2, 178_460, 1), Src("female", 3, 3, 181_120, 2), Src("female", 2, 1, 185_670, 3),
            Src("male", 3, 4, 158_390, 1),
        };
        var db = new[]
        {
            Db(1, "female", 3, 2, 178_460, 4), Db(2, "female", 3, 3, 181_120, 5), Db(3, "female", 2, 1, 185_670, 8),
            Db(4, "male", 3, 4, 158_390, 1),
        };

        var plan = LogligPlaceMatcher.Build(source, db);

        Assert.True(plan.CanApply);
        Assert.Empty(plan.Mismatches);
        Assert.Empty(plan.Unmatched);
        var after = plan.Changes.ToDictionary(c => c.ResultId, c => c.PositionAfter);
        Assert.Equal(new int?[] { 1, 2, 3, 1 }, new[] { after[1], after[2], after[3], after[4] });
        Assert.Equal(3, plan.Changes.Count(c => c.HasChanges));   // мальчик уже стоял верно
    }

    /// <summary>
    /// «כללי» — это все без своей возрастной секции, разных годов вместе: 18-летний (2009) на
    /// 1-м месте у источника не отнимает золото у 12-летнего (2015). 16891, 50 баттерфляем:
    /// מטביי קוצירנקו 2009 — 1, דניאל קיננסברונר 2015 — 1 (был 2), אביעד לוקה 2015 — 2 (был 3).
    /// </summary>
    [Fact]
    public void OpenSection_PlacesAreCountedWithinBirthYear()
    {
        // Секции «בנים 11» (2016) и «בנים 13» (2014) есть, а 12-летние (2015) — в «כללי»: пропуск
        // в лестнице секций, остаток без своей секции.
        var source = new[]
        {
            Src("male", 12, 3, 30_990, 1, birthYear: 2009),
            Src("male", 12, 5, 36_820, 2, birthYear: 2015),
            Src("male", 10, 4, 37_180, 3, birthYear: 2015),
            Src("male", 9, 1, 34_000, 1, "בנים 13", birthYear: 2014),
            Src("male", 8, 1, 40_000, 1, "בנים 11", birthYear: 2016),
        };
        var db = new[]
        {
            Db(1, "male", 12, 3, 30_990, 1, birthYear: 2009),
            Db(2, "male", 12, 5, 36_820, 2, birthYear: 2015),
            Db(3, "male", 10, 4, 37_180, 3, birthYear: 2015),
            Db(4, "male", 9, 1, 34_000, 1, birthYear: 2014),
            Db(5, "male", 8, 1, 40_000, 1, birthYear: 2016),
        };

        var after = LogligPlaceMatcher.Build(source, db).Changes.ToDictionary(c => c.ResultId, c => c.PositionAfter);

        Assert.Equal(new int?[] { 1, 1, 2 }, new[] { after[1], after[2], after[3] });
    }

    /// <summary>
    /// Лига (ראשון לציון, 2025/26): секции 9–13, а в «כללי» только старшие, 14–18 лет — это общий
    /// зачёт старших. Место — как на странице loglig, внутри пола, без деления по году (решение
    /// Влада 07.11.2026): иначе вышло бы по «первому месту» на каждый год.
    /// </summary>
    [Fact]
    public void OpenSection_OnlyOlderThanAllSections_PlacesAreGenderOnly()
    {
        const string day = "28/11/2025";
        PlaceRowFromSource S(int lane, int ms, int pos, string cat, int by) =>
            new(day, "breaststroke", "50", "female", cat, 1, lane, "name", by, ms, pos);
        PlaceRowInDb D(long id, int lane, int ms, int pos, int by) =>
            new(id, day, "breaststroke", "50", "female", 1, lane, "name", by, ms, pos);

        var source = new[]
        {
            S(1, 36_000, 1, "כללי", 2009), S(2, 37_000, 2, "כללי", 2011), S(3, 38_000, 3, "כללי", 2008),
            S(4, 40_000, 1, "בנות 13", 2013),
        };
        // У нас места «כללי» сквозные через оба пола (мальчики между ними): 2, 4, 7.
        var db = new[] { D(1, 1, 36_000, 2, 2009), D(2, 2, 37_000, 4, 2011), D(3, 3, 38_000, 7, 2008), D(4, 4, 40_000, 1, 2013) };

        var after = LogligPlaceMatcher.Build(source, db).Changes.ToDictionary(c => c.ResultId, c => c.PositionAfter);

        Assert.Equal(new int?[] { 1, 2, 3, 1 }, new[] { after[1], after[2], after[3], after[4] });
    }

    /// <summary>
    /// Национальный чемпионат (273, лето 2025, 1500 в/с): секции 15, 16, «17-18», а в «כללי» —
    /// взрослые и один 14-летний. Он младше самой младшей секции, плывёт «вверх» в открытом
    /// зачёте; это не остаток. Место — общее, как на странице loglig (решение Влада 08.11.2026),
    /// а не «1-е место» каждому году.
    /// </summary>
    [Fact]
    public void OpenSection_YoungerThanAllSections_IsOpenRanking_NotSplitByYear()
    {
        const string day = "24/07/2025";
        PlaceRowFromSource S(int lane, int ms, int pos, string cat, int by) =>
            new(day, "freestyle", "1500", "male", cat, 1, lane, "name", by, ms, pos);
        PlaceRowInDb D(long id, int lane, int ms, int pos, int by) =>
            new(id, day, "freestyle", "1500", "male", 1, lane, "name", by, ms, pos);

        var source = new[]
        {
            S(1, 950_000, 1, "כללי", 1998), S(2, 960_000, 2, "כללי", 2003), S(3, 970_000, 3, "כללי", 2011),
            S(4, 990_000, 1, "בנים 15", 2010), S(5, 995_000, 1, "בנים 16", 2009),
            S(6, 980_000, 1, "גברים 17-18", 2007),
        };
        var db = new[]
        {
            D(1, 1, 950_000, 1, 1998), D(2, 2, 960_000, 2, 2003), D(3, 3, 970_000, 3, 2011),
            D(4, 4, 990_000, 1, 2010), D(5, 5, 995_000, 1, 2009), D(6, 6, 980_000, 1, 2007),
        };

        var plan = LogligPlaceMatcher.Build(source, db);

        Assert.DoesNotContain(plan.Changes, c => c.HasChanges);
    }

    /// <summary>
    /// Снятый в середине секции места не занимает: страница нумерует его 2-м, а следующих 3-м и
    /// 4-м (у 16891 — 11, 12, 13 в 200 комплексом), у нас и в PDF они 2-й и 3-й.
    /// </summary>
    [Fact]
    public void DisqualifiedInSection_DoesNotTakeAPlace()
    {
        var source = new[]
        {
            Src("male", 7, 1, 178_770, 1, "בנים 14"),
            Src("male", 3, 3, null, 2, "בנים 14"),
            Src("male", 3, 2, 183_120, 3, "בנים 14"),
            Src("male", 3, 4, 190_250, 4, "בנים 14"),
        };
        var db = new[]
        {
            Db(1, "male", 7, 1, 178_770, 1), Db(2, "male", 3, 3, null, null),
            Db(3, "male", 3, 2, 183_120, 2), Db(4, "male", 3, 4, 190_250, 3),
        };

        var plan = LogligPlaceMatcher.Build(source, db);

        Assert.DoesNotContain(plan.Changes, c => c.HasChanges);
        Assert.Null(plan.Changes.Single(c => c.ResultId == 2).PositionAfter);
    }

    /// <summary>
    /// Р90: двое «00:00.00» без статуса на 1-м месте (16891, 50 вольным мальчики 13) — места
    /// настоящие, первый со временем — 3-й, как напечатано. Ничего не правится.
    /// </summary>
    [Fact]
    public void PlacedWithoutTime_KeepsPrintedPlaces()
    {
        var source = new[]
        {
            Src("male", 11, 1, null, 1, "בנים 13") with { PlacedWithoutTime = true },
            Src("male", 22, 2, null, 1, "בנים 13") with { PlacedWithoutTime = true },
            Src("male", 27, 3, 29_620, 3, "בנים 13"),
        };
        var db = new[]
        {
            Db(1, "male", 11, 1, null, 1), Db(2, "male", 22, 2, null, 1), Db(3, "male", 27, 3, 29_620, 3),
        };

        var plan = LogligPlaceMatcher.Build(source, db);

        Assert.DoesNotContain(plan.Changes, c => c.HasChanges);
        Assert.Equal(new int?[] { 1, 1, 3 }, plan.Changes.OrderBy(c => c.ResultId).Select(c => c.PositionAfter));
    }

    /// <summary>Ничья у источника (два вторых места) остаётся ничьей.</summary>
    [Fact]
    public void Tie_KeepsSharedPlace()
    {
        var source = new[]
        {
            Src("female", 1, 1, 60_000, 1), Src("female", 1, 2, 61_000, 2), Src("female", 1, 3, 61_000, 2),
            Src("female", 1, 4, 62_000, 4),
        };
        var db = new[]
        {
            Db(1, "female", 1, 1, 60_000, 1), Db(2, "female", 1, 2, 61_000, 3), Db(3, "female", 1, 3, 61_000, 4),
            Db(4, "female", 1, 4, 62_000, 7),
        };

        var after = LogligPlaceMatcher.Build(source, db).Changes.ToDictionary(c => c.ResultId, c => c.PositionAfter);

        Assert.Equal(new int?[] { 1, 2, 2, 4 }, new[] { after[1], after[2], after[3], after[4] });
    }

    /// <summary>
    /// Время разошлось (организатор поправил протокол после забора — בן להמן 34.62 → 33.05):
    /// место такой строки не трогаем, она идёт в расхождения, остальные пары пишутся.
    /// </summary>
    [Fact]
    public void TimeMismatch_RowSkippedAndReported_OthersStillApply()
    {
        var source = new[] { Src("male", 12, 1, 33_050, 1, "בנים 13"), Src("male", 10, 1, 34_170, 2, "בנים 13") };
        var db = new[] { Db(1, "male", 12, 1, 34_620, 3), Db(2, "male", 10, 1, 34_170, 1) };

        var plan = LogligPlaceMatcher.Build(source, db);

        Assert.True(plan.CanApply);
        var mismatch = Assert.Single(plan.Mismatches);
        Assert.Contains("00:34.62", mismatch);
        Assert.Contains("00:33.05", mismatch);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(2, change.ResultId);
        Assert.Equal(2, change.PositionAfter);
    }

    /// <summary>
    /// Возрастная секция: расхождение — это напечатанное в протоколе (у 16891 двое без времени с
    /// 1-м местом в 50 вольным, мальчики 13), ремонт его показывает, но не пишет (Р76).
    /// «כללי» — пишет.
    /// </summary>
    [Fact]
    public void AgeSectionChange_ReportedNotApplied_OpenSectionApplied()
    {
        var source = new[]
        {
            Src("male", 11, 1, null, 1, "בנים 13"), Src("male", 27, 3, 29_620, 3, "בנים 13"),
            Src("female", 2, 1, 185_670, 3),
        };
        var db = new[]
        {
            Db(1, "male", 11, 1, null, null), Db(2, "male", 27, 3, 29_620, 3),
            Db(3, "female", 2, 1, 185_670, 8),
        };

        var changes = LogligPlaceMatcher.Build(source, db).Changes.ToDictionary(c => c.ResultId);

        Assert.True(changes[2].HasChanges);      // 3 → 1 по счёту секции
        Assert.False(changes[2].WillApply);      // но это возрастная секция
        Assert.True(changes[3].WillApply);       // «כללי»: 8 → 1
        Assert.Equal(1, changes[3].PositionAfter);
    }

    /// <summary>
    /// Чемпионат (comp 1497, «Арена» младшие, 24/02/2026): у loglig отдельное событие на каждый
    /// возраст, заплывы в каждом с 1. «100 брассом, девочки, заплыв 3, дорожка 4» — три девочки из
    /// событий 11, 12 и 13 лет. Строки сводятся по времени и году; места — внутри своего события.
    /// </summary>
    [Fact]
    public void SharedKeyAcrossAgeEvents_PairedByTimeAndYear_PlacesWithinEvent()
    {
        PlaceRowFromSource S(string ev, int ms, int pos, int by) =>
            new(Day, "breaststroke", "100", "female", "", 3, 4, "name", by, ms, pos, Event: ev, Round: "final");
        var source = new[]
        {
            S("76820", 90_310, 1, 2015), S("76831", 82_320, 1, 2014), S("76841", 83_510, 2, 2013),
            new PlaceRowFromSource(Day, "breaststroke", "100", "female", "", 2, 4, "name", 2013, 82_000, 1,
                Event: "76841", Round: "final"),
        };
        PlaceRowInDb D(long id, int heat, int ms, int pos, int by) =>
            new(id, Day, "breaststroke", "100", "female", heat, 4, "name", by, ms, pos);
        var db = new[] { D(1, 3, 82_320, 1, 2014), D(2, 3, 83_510, 2, 2013), D(3, 3, 90_310, 1, 2015), D(4, 2, 82_000, 1, 2013) };

        var plan = LogligPlaceMatcher.Build(source, db);

        Assert.True(plan.CanApply);
        Assert.Empty(plan.Mismatches);
        Assert.Empty(plan.Unmatched);
        Assert.DoesNotContain(plan.Changes, c => c.HasChanges);   // 82.32 — 1-я среди 12-леток, не 2-я общая
    }

    /// <summary>Предварительные и финал на одной дорожке у одного пловца — разные времена, две пары.</summary>
    [Fact]
    public void PrelimAndFinal_SameLane_PairedByTime()
    {
        var source = new[]
        {
            Src("female", 1, 4, 61_000, 3, "בנות 14") with { Round = "prelim" },
            Src("female", 1, 4, 60_000, 1, "בנות 14") with { Round = "final" },
        };
        var db = new[] { Db(1, "female", 1, 4, 60_000, 1), Db(2, "female", 1, 4, 61_000, 1) };

        var plan = LogligPlaceMatcher.Build(source, db);

        Assert.True(plan.CanApply);
        var after = plan.Changes.ToDictionary(c => c.ResultId, c => c.PositionAfter);
        Assert.Equal(1, after[1]);   // финал: 1-я в своём раунде
        Assert.Equal(1, after[2]);   // предварительные: единственная в выборке раунда → 1
    }

    /// <summary>Общий ключ, а доказательства не сошлись ни у кого — пары не доказаны, план не пишется.</summary>
    [Fact]
    public void SharedKey_NoProof_BlocksApply()
    {
        var source = new[] { Src("female", 1, 1, 60_000, 1), Src("female", 1, 1, 61_000, 2) };
        var db = new[] { Db(1, "female", 1, 1, 59_000, 1), Db(2, "female", 1, 1, 62_000, 2) };

        var plan = LogligPlaceMatcher.Build(source, db);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Problems, p => p.Contains("пары не доказаны"));
    }

    /// <summary>Общий ключ: одну пару доказало время, остаток один-на-один — пара с расхождением.</summary>
    [Fact]
    public void SharedKey_OneProven_RestIsMismatch()
    {
        var source = new[] { Src("female", 1, 1, 60_000, 1), Src("female", 1, 1, 61_000, 2) };
        var db = new[] { Db(1, "female", 1, 1, 60_000, 1), Db(2, "female", 1, 1, 61_500, 2) };

        var plan = LogligPlaceMatcher.Build(source, db);

        Assert.True(plan.CanApply);
        Assert.Single(plan.Mismatches);
    }

    [Theory]
    [InlineData("בנות 14", "none", "female")]
    [InlineData("נשים 13-99", "none", "female")]
    [InlineData("בנים 13", "female", "male")]
    [InlineData("כללי", "female", "female")]
    [InlineData("כללי", "male", "male")]
    public void SectionGender_FromSectionElseEventHeader(string category, string eventGender, string expected) =>
        Assert.Equal(expected, LogligPlaceMatcher.SectionGender(category, eventGender));
}
