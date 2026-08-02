using System;
using System.Collections.Generic;
using System.Linq;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Проверки достоверности результатов. Ловят ошибки САМОГО источника: протокол напечатан
/// так, как напечатан, парсером это не лечится. Эталонный случай — Маккабиада 2026, где у
/// Elisa MOSHKOVITCH на 100 м баттерфляем стоит 00:32.59 (её же полтинник — 27.27), и
/// организаторы посчитали из этого времени 4702 очка; заплыв «бил» национальный рекорд.
/// </summary>
public class SuspectResultDetectorTests
{
    private static readonly DateTime Day1 = new(2026, 7, 5);

    private static SuspectCandidateRow Row(
        long id, int ms, string style = "butterfly", string distance = "100",
        string gender = "female", int swimmerId = 1, DateTime? date = null,
        bool isRelay = false, bool timeFail = false, string? ageGroup = null)
        => new(id, swimmerId, style, distance, gender, ms, date ?? Day1, isRelay, timeFail, ageGroup);

    /// <summary>Правдоподобный «фон» заплыва, чтобы медиана была осмысленной.</summary>
    private static IEnumerable<SuspectCandidateRow> Field(int startId, params int[] times)
        => times.Select((ms, i) => Row(startId + i, ms, swimmerId: 100 + startId + i));

    [Fact]
    public void FasterThanWorldRecord_Flagged()
    {
        var rows = new[] { Row(1, 32_590) }.Concat(Field(10, 57_330, 65_690, 66_690, 67_020)).ToList();

        var v = Assert.Single(SuspectResultDetector.Detect(rows), x => x.ResultId == 1);
        Assert.Equal(SuspectReasons.TimeVsDistance, v.Reason);
        Assert.Contains("мирового рекорда", v.Note);
    }

    [Fact]
    public void PlausibleField_NotFlagged()
    {
        var rows = Field(10, 57_330, 65_690, 66_690, 67_020, 87_240).ToList();
        Assert.Empty(SuspectResultDetector.Detect(rows));
    }

    [Fact]
    public void WorldRecordThreshold_IsGenderSpecific()
    {
        // 00:53.42 на 100 м баттерфляем (реальная строка протокола Маккабиады): быстрее
        // ЖЕНСКОГО мирового рекорда 54.60, но медленнее мужского 47.78. По мужскому порогу
        // такие ошибки проходили незамеченными.
        var women = new[] { Row(1, 53_420, gender: "female") }
            .Concat(Field(10, 57_330, 65_690, 66_690, 67_020)).ToList();
        var v = Assert.Single(SuspectResultDetector.Detect(women), x => x.ResultId == 1);
        Assert.Equal(SuspectReasons.TimeVsDistance, v.Reason);
        Assert.Contains("54.60", v.Note);

        // То же время у мужчины — законный сильный результат, не помечается.
        var men = new[] { Row(1, 53_420, gender: "male", swimmerId: 3) }
            .Concat(Enumerable.Range(0, 4).Select(i =>
                Row(20 + i, 55_000 + i * 2000, gender: "male", swimmerId: 200 + i))).ToList();
        Assert.DoesNotContain(SuspectResultDetector.Detect(men), x => x.ResultId == 1);
    }

    [Fact]
    public void Outlier_FlaggedEvenWhenDistanceHasNoWorldRecordReference()
    {
        // 4X100 в WorldBestMs нет, поэтому сработать может только правило медианы.
        var rows = new[] { Row(1, 31_950, style: "medley_unknown", distance: "4X100") }
            .Concat(Enumerable.Range(0, 5).Select(i =>
                Row(10 + i, 240_000 + i * 1000, style: "medley_unknown", distance: "4X100", swimmerId: 50 + i)))
            .ToList();

        var v = Assert.Single(SuspectResultDetector.Detect(rows), x => x.ResultId == 1);
        Assert.Equal(SuspectReasons.TimeOutlier, v.Reason);
    }

    [Fact]
    public void Outlier_MedianIsPerAgeGroup_NotWholeDiscipline()
    {
        // Реальная детская лига (competition 1516, «ליגה מס 2- הפועל ירושלים», 50 вольным
        // мужчины): в одной дисциплине плывут восьмилетки и семнадцатилетние. Медиана по всем
        // строкам — 43.53, и победители старшей ступени (25.25 и 26.03) оказывались быстрее
        // 60% от неё. Внутри своей ступени (медиана ~30.06) они совершенно нормальны.
        var rows = new List<SuspectCandidateRow>
        {
            Row(1, 25_250, "freestyle", "50", "male", swimmerId: 1, ageGroup: "17-18"),
            Row(2, 26_030, "freestyle", "50", "male", swimmerId: 2, ageGroup: "17-18"),
            Row(3, 28_440, "freestyle", "50", "male", swimmerId: 3, ageGroup: "17-18"),
            Row(4, 30_060, "freestyle", "50", "male", swimmerId: 4, ageGroup: "17-18"),
            Row(5, 33_100, "freestyle", "50", "male", swimmerId: 5, ageGroup: "17-18"),
            // Младших больше — как в жизни: именно они и утягивают общую медиану вниз.
            Row(6, 40_210, "freestyle", "50", "male", swimmerId: 6, ageGroup: "9-10"),
            Row(7, 44_000, "freestyle", "50", "male", swimmerId: 7, ageGroup: "9-10"),
            Row(8, 49_370, "freestyle", "50", "male", swimmerId: 8, ageGroup: "0-8"),
            Row(9, 55_200, "freestyle", "50", "male", swimmerId: 9, ageGroup: "0-8"),
            Row(10, 59_095, "freestyle", "50", "male", swimmerId: 10, ageGroup: "0-8"),
            Row(11, 64_800, "freestyle", "50", "male", swimmerId: 11, ageGroup: "0-8"),
            Row(12, 70_000, "freestyle", "50", "male", swimmerId: 12, ageGroup: "9-10"),
        };

        // Общая медиана тут 44.00 → старый порог 26.40 пометил бы обе верхние строки.
        Assert.Empty(SuspectResultDetector.Detect(rows));
    }

    [Fact]
    public void Outlier_RealErrorInsideAgeGroup_StillFlagged()
    {
        // Сужение группы не должно ослабить правило: ошибка протокола выбивается и внутри
        // своей ступени. Соседняя ступень с быстрыми временами больше ни на что не влияет.
        var rows = new List<SuspectCandidateRow>
        {
            Row(1, 22_000, "freestyle", "50", "male", swimmerId: 1, ageGroup: "0-8"),
            Row(2, 55_200, "freestyle", "50", "male", swimmerId: 2, ageGroup: "0-8"),
            Row(3, 59_095, "freestyle", "50", "male", swimmerId: 3, ageGroup: "0-8"),
            Row(4, 64_800, "freestyle", "50", "male", swimmerId: 4, ageGroup: "0-8"),
            Row(5, 25_250, "freestyle", "50", "male", swimmerId: 5, ageGroup: "17-18"),
            Row(6, 26_030, "freestyle", "50", "male", swimmerId: 6, ageGroup: "17-18"),
            Row(7, 28_440, "freestyle", "50", "male", swimmerId: 7, ageGroup: "17-18"),
            Row(8, 30_060, "freestyle", "50", "male", swimmerId: 8, ageGroup: "17-18"),
        };

        var v = Assert.Single(SuspectResultDetector.Detect(rows), x => x.ResultId == 1);
        Assert.Equal(SuspectReasons.TimeOutlier, v.Reason);
        Assert.Contains("ступени 0-8", v.Note);
    }

    [Fact]
    public void Relays_And_FailedTimes_Ignored()
    {
        var rows = new[]
        {
            Row(1, 31_950, isRelay: true),          // эстафета — вне скоупа
            Row(2, 1_000, timeFail: true),          // DSQ/DNS
            Row(3, 0),                              // нет времени
        };
        Assert.Empty(SuspectResultDetector.Detect(rows));
    }

    [Fact]
    public void GenderMismatch_FlaggedAgainstSwimmersOtherSwims()
    {
        // Пловец в трёх заплывах female и в одном male — помечается именно четвёртый.
        var rows = new List<SuspectCandidateRow>
        {
            Row(1, 26_340, "freestyle", "50", "female", swimmerId: 7),
            Row(2, 27_270, "butterfly", "50", "female", swimmerId: 7),
            Row(3, 58_320, "freestyle", "100", "female", swimmerId: 7),
            Row(4, 59_000, "backstroke", "100", "male", swimmerId: 7),
        };

        var v = Assert.Single(SuspectResultDetector.Detect(rows), x => x.ResultId == 4);
        Assert.Equal(SuspectReasons.GenderMismatch, v.Reason);
        Assert.Contains("female", v.Note);
    }

    [Fact]
    public void DuplicateSwim_SameDayFlagged_DifferentDaysNot()
    {
        var sameDay = new[]
        {
            Row(1, 60_000, swimmerId: 7),
            Row(2, 61_000, swimmerId: 7),
        };
        var flagged = SuspectResultDetector.Detect(sameDay);
        Assert.Equal(2, flagged.Count);
        Assert.All(flagged, v => Assert.Equal(SuspectReasons.DuplicateSwim, v.Reason));

        // Повтор дисциплины в РАЗНЫЕ дни — норма (предварительные/финал).
        var otherDays = new[]
        {
            Row(1, 60_000, swimmerId: 7),
            Row(2, 61_000, swimmerId: 7, date: Day1.AddDays(1)),
        };
        Assert.Empty(SuspectResultDetector.Detect(otherDays));
    }

    [Fact]
    public void OneRowGetsOneReason()
    {
        // 32.59 на сотне подходит и под «быстрее МР», и под «время отрезка», и под выброс —
        // причина должна быть ровно одна, иначе пометка не отвечает на «почему».
        var rows = new[] { Row(1, 32_590) }.Concat(Field(10, 53_420, 57_330, 65_690, 66_690)).ToList();
        var all = SuspectResultDetector.Detect(rows).Where(v => v.ResultId == 1).ToList();
        Assert.Single(all);
    }
}
