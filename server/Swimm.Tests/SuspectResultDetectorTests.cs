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
        bool isRelay = false, bool timeFail = false, string? ageGroup = null,
        string? heatType = null, string? pool = "50m")
        => new(id, swimmerId, style, distance, gender, ms, date ?? Day1, isRelay, timeFail, ageGroup,
            HeatType: heatType, PoolType: pool);

    /* ── Справочник мировых рекордов ──────────────────────────────────────────────
     * Фикстура, а НЕ копия продакшн-таблицы: пороги приезжают из справочника Records
     * (WorldBestReference), и тесту нужен свой воспроизводимый набор, который не поедет
     * при следующем обновлении рекордов. Значения взяты настоящие, обе воды — на них
     * держится проверка «мерим по своему бассейну».
     *
     * 100 к/п лежит ТОЛЬКО в короткой воде: в длинной такой дистанции не существует.
     * На нём и проверяется фолбэк «рекорда 50 м нет — сверяем по 25 м и говорим об этом».
     */
    private static readonly WorldBestReference Wr = WorldBestReference.Build(
    [
        ("male", "freestyle", "50m", "50m", "20.88"),      ("male", "freestyle", "50m", "25m", "19.90"),
        ("female", "freestyle", "50m", "50m", "23.55"),    ("female", "freestyle", "50m", "25m", "22.83"),
        ("male", "freestyle", "100m", "50m", "46.40"),     ("male", "freestyle", "100m", "25m", "44.84"),
        ("female", "freestyle", "100m", "50m", "51.68"),   ("female", "freestyle", "100m", "25m", "49.93"),
        ("male", "freestyle", "200m", "50m", "01:42.00"),  ("male", "freestyle", "200m", "25m", "01:38.61"),
        ("female", "freestyle", "200m", "50m", "01:52.23"),("female", "freestyle", "200m", "25m", "01:49.36"),
        ("male", "backstroke", "50m", "50m", "23.55"),     ("male", "backstroke", "50m", "25m", "22.11"),
        ("female", "backstroke", "50m", "50m", "26.86"),   ("female", "backstroke", "50m", "25m", "25.23"),
        ("male", "backstroke", "100m", "50m", "51.60"),    ("male", "backstroke", "100m", "25m", "48.16"),
        ("female", "backstroke", "100m", "50m", "57.13"),  ("female", "backstroke", "100m", "25m", "54.02"),
        ("male", "butterfly", "50m", "50m", "22.27"),      ("male", "butterfly", "50m", "25m", "21.32"),
        ("female", "butterfly", "50m", "50m", "24.43"),    ("female", "butterfly", "50m", "25m", "23.72"),
        ("male", "butterfly", "100m", "50m", "49.45"),     ("male", "butterfly", "100m", "25m", "47.68"),
        ("female", "butterfly", "100m", "50m", "54.33"),   ("female", "butterfly", "100m", "25m", "52.71"),
        ("male", "breaststroke", "200m", "50m", "02:05.48"),   ("male", "breaststroke", "200m", "25m", "01:59.52"),
        ("female", "breaststroke", "200m", "50m", "02:17.55"), ("female", "breaststroke", "200m", "25m", "02:12.50"),
        ("male", "individual_medley", "200m", "50m", "01:52.69"), ("male", "individual_medley", "200m", "25m", "01:48.88"),
        ("male", "individual_medley", "100m", "25m", "49.28"),
        ("female", "individual_medley", "100m", "25m", "55.11"),
    ]);

    /// <summary>Обёртка: все тесты класса ходят в детектор с фикстурой рекордов выше.</summary>
    private static List<SuspectVerdict> Detect(
        IReadOnlyCollection<SuspectCandidateRow> rows,
        IReadOnlyDictionary<int, IReadOnlyList<PersonalSwim>>? history = null)
        => SuspectResultDetector.Detect(rows, history, Wr);

    /// <summary>Правдоподобный «фон» заплыва, чтобы медиана была осмысленной.</summary>
    private static IEnumerable<SuspectCandidateRow> Field(int startId, params int[] times)
        => times.Select((ms, i) => Row(startId + i, ms, swimmerId: 100 + startId + i));

    [Fact]
    public void FasterThanWorldRecord_Flagged()
    {
        var rows = new[] { Row(1, 32_590) }.Concat(Field(10, 57_330, 65_690, 66_690, 67_020)).ToList();

        var v = Assert.Single(Detect(rows), x => x.ResultId == 1);
        Assert.Equal(SuspectReasons.TimeVsDistance, v.Reason);
        Assert.Contains("мирового рекорда", v.Note);
    }

    [Fact]
    public void PlausibleField_NotFlagged()
    {
        var rows = Field(10, 57_330, 65_690, 66_690, 67_020, 87_240).ToList();
        Assert.Empty(Detect(rows));
    }

    [Fact]
    public void WorldRecordThreshold_IsGenderSpecific()
    {
        // 00:53.42 на 100 м баттерфляем (реальная строка протокола Маккабиады): быстрее
        // ЖЕНСКОГО мирового рекорда длинной воды 54.33, но медленнее мужского 49.45. По мужскому порогу
        // такие ошибки проходили незамеченными.
        var women = new[] { Row(1, 53_420, gender: "female") }
            .Concat(Field(10, 57_330, 65_690, 66_690, 67_020)).ToList();
        var v = Assert.Single(Detect(women), x => x.ResultId == 1);
        Assert.Equal(SuspectReasons.TimeVsDistance, v.Reason);
        Assert.Contains("54.33", v.Note);

        // То же время у мужчины — законный сильный результат, не помечается.
        var men = new[] { Row(1, 53_420, gender: "male", swimmerId: 3) }
            .Concat(Enumerable.Range(0, 4).Select(i =>
                Row(20 + i, 55_000 + i * 2000, gender: "male", swimmerId: 200 + i))).ToList();
        Assert.DoesNotContain(Detect(men), x => x.ResultId == 1);
    }

    [Fact]
    public void Outlier_FlaggedEvenWhenDistanceHasNoWorldRecordReference()
    {
        // 4X100 в WorldBestMs нет, поэтому сработать может только правило медианы.
        var rows = new[] { Row(1, 31_950, style: "medley_unknown", distance: "4X100") }
            .Concat(Enumerable.Range(0, 5).Select(i =>
                Row(10 + i, 240_000 + i * 1000, style: "medley_unknown", distance: "4X100", swimmerId: 50 + i)))
            .ToList();

        var v = Assert.Single(Detect(rows), x => x.ResultId == 1);
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
        Assert.Empty(Detect(rows));
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

        var v = Assert.Single(Detect(rows), x => x.ResultId == 1);
        Assert.Equal(SuspectReasons.TimeOutlier, v.Reason);
        Assert.Contains("ступени 0-8", v.Note);
    }

    [Fact]
    public void Outlier_FastKidInWeakHeat_NotFlagged()
    {
        // Живой случай (competition 1590 «ליגה מס 1 הפועל בית שמש», 50 вольным, ступень 9-10):
        // разброс внутри ступени двукратный, медиана 1:18.31 — половина группы едва плывёт.
        // Победительница 46.89 ниже 60% медианы, но от второго результата (52.77) отстоит
        // на 11%: это просто сильный ребёнок, а не ошибка протокола.
        var rows = new List<SuspectCandidateRow>
        {
            Row(1, 46_890, "freestyle", "50", "female", swimmerId: 1, ageGroup: "9-10"),
            Row(2, 52_770, "freestyle", "50", "female", swimmerId: 2, ageGroup: "9-10"),
            Row(3, 64_800, "freestyle", "50", "female", swimmerId: 3, ageGroup: "9-10"),
            Row(4, 78_310, "freestyle", "50", "female", swimmerId: 4, ageGroup: "9-10"),
            Row(5, 94_980, "freestyle", "50", "female", swimmerId: 5, ageGroup: "9-10"),
            Row(6, 101_140, "freestyle", "50", "female", swimmerId: 6, ageGroup: "9-10"),
        };

        Assert.Empty(Detect(rows));
    }

    [Fact]
    public void Outlier_HalfDistanceTime_StillFlagged()
    {
        // Ради чего правило живёт: в протокол попало время отрезка — оно примерно вдвое
        // меньше соседнего результата, а не на проценты. Само по себе оно правдоподобно
        // (медленнее мирового рекорда), поэтому правила 1 и 2 его не видят.
        var rows = new List<SuspectCandidateRow>
        {
            Row(1, 55_000, "freestyle", "100", "female", swimmerId: 1, ageGroup: "9-10"),
            Row(2, 130_000, "freestyle", "100", "female", swimmerId: 2, ageGroup: "9-10"),
            Row(3, 140_000, "freestyle", "100", "female", swimmerId: 3, ageGroup: "9-10"),
            Row(4, 150_000, "freestyle", "100", "female", swimmerId: 4, ageGroup: "9-10"),
        };

        var v = Assert.Single(Detect(rows), x => x.ResultId == 1);
        Assert.Equal(SuspectReasons.TimeOutlier, v.Reason);
        Assert.Contains("ближайшем результате", v.Note);
    }

    [Fact]
    public void GenderMismatch_TwoSwims_LeansOnSwimmerCard()
    {
        // Живой случай (comp 1580): у пловца ровно два старта — брасс записан женским,
        // комплекс мужским. По большинству 1:1 «меньшинством» оказывалась случайная строка,
        // и у טנא יהלי (male по карточке и по 32 другим заплывам) обвинялась мужская.
        var rows = new List<SuspectCandidateRow>
        {
            new(1, 7, "breaststroke", "200", "female", 159_240, Day1, false, false, "15-16",
                SwimmerGender: "male"),
            new(2, 7, "individual_medley", "200", "male", 152_950, Day1, false, false, "15-16",
                SwimmerGender: "male"),
        };

        var v = Assert.Single(Detect(rows));
        Assert.Equal(1, v.ResultId);
        Assert.Equal(SuspectReasons.GenderMismatch, v.Reason);
        Assert.Contains("по карточке пловца", v.Note);
    }

    [Fact]
    public void GenderMismatch_NoCardGender_FallsBackToMajority()
    {
        // Пол в карточке не заполнен (в базе такие есть) — опора прежняя: большинство
        // по заплывам этого соревнования.
        var rows = new List<SuspectCandidateRow>
        {
            new(1, 7, "freestyle", "50", "female", 31_000, Day1, false, false, null),
            new(2, 7, "freestyle", "100", "male", 68_000, Day1, false, false, null),
            new(3, 7, "backstroke", "50", "male", 38_000, Day1, false, false, null),
        };

        var v = Assert.Single(Detect(rows));
        Assert.Equal(1, v.ResultId);
        Assert.Contains("в остальных заплывах пловца", v.Note);
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
        Assert.Empty(Detect(rows));
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

        var v = Assert.Single(Detect(rows), x => x.ResultId == 4);
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
        var flagged = Detect(sameDay);
        Assert.Equal(2, flagged.Count);
        Assert.All(flagged, v => Assert.Equal(SuspectReasons.DuplicateSwim, v.Reason));

        // Повтор дисциплины в РАЗНЫЕ дни — норма (предварительные/финал).
        var otherDays = new[]
        {
            Row(1, 60_000, swimmerId: 7),
            Row(2, 61_000, swimmerId: 7, date: Day1.AddDays(1)),
        };
        Assert.Empty(Detect(otherDays));
    }

    [Fact]
    public void DuplicateSwim_PrelimPlusFinalSameDayNotFlagged_SameSessionStillFlagged()
    {
        // Бугрим: предварительные и финал одной дисциплины в ОДИН день — норма
        // (1678 ложных пометок на чемпионате 2026 до появления HeatType).
        var prelimFinal = new[]
        {
            Row(1, 60_000, swimmerId: 7, heatType: "prelim"),
            Row(2, 59_500, swimmerId: 7, heatType: "final"),
        };
        Assert.Empty(Detect(prelimFinal));

        // Дубль ВНУТРИ одной сессии по-прежнему ловится.
        var withinFinal = new[]
        {
            Row(1, 60_000, swimmerId: 7, heatType: "final"),
            Row(2, 61_000, swimmerId: 7, heatType: "final"),
        };
        var flagged = Detect(withinFinal);
        Assert.Equal(2, flagged.Count);
        Assert.All(flagged, v => Assert.Equal(SuspectReasons.DuplicateSwim, v.Reason));
    }

    [Fact]
    public void DuplicateSwim_ExtraHeatAfterFinal_NotFlagged()
    {
        // Герцлия 01/11/2025: 50 вольным плыли трижды — прелимы, финал и призовая серия на
        // выбывание. Пока skins считался прелимом, он попадал с утренним заплывом в одну
        // «сессию», и проверка помечала законные строки «повтором дисциплины за день».
        var threeSessions = new[]
        {
            Row(1, 22_850, style: "freestyle", distance: "50", swimmerId: 7, heatType: "prelim"),
            Row(2, 23_040, style: "freestyle", distance: "50", swimmerId: 7, heatType: "final"),
            Row(3, 23_180, style: "freestyle", distance: "50", swimmerId: 7, heatType: "extra"),
        };

        var flagged = Detect(threeSessions);
        Assert.DoesNotContain(flagged, v => v.Reason == SuspectReasons.DuplicateSwim);
    }

    /* ── Выброс относительно личных результатов (Б1) ───────────────────────────────
     * Живой случай: 200 вольным за 01:53.09 (678 очков) у пловца, чей лучший результат
     * тех же месяцев — 312 очков. Рекорда не бьёт, от медианы заплыва недалеко — ни одно
     * из прежних правил его не видит. Порог 2.0 калиброван на живой базе: он даёт 5 находок
     * на 26 тыс. заплывов, при 1.5 их было бы 20.
     */
    private static SuspectCandidateRow PointRow(long id, int points, DateTime? date = null, int swimmerId = 7)
        => new(id, swimmerId, "freestyle", "200", "male", 113_090, date ?? Day1, false, false, "13-14", points);

    /// <summary>Строка с номером заплыва — для страховки «согласовано со своим заплывом».</summary>
    private static SuspectCandidateRow HeatRow(
        long id, int ms, int heat, int swimmerId, int? points = null, string distance = "50")
        => new(id, swimmerId, "freestyle", distance, "male", ms, Day1, false, false, "45-49",
            points, Heat: heat);

    private static IReadOnlyDictionary<int, IReadOnlyList<PersonalSwim>> History(params PersonalSwim[] swims)
        => new Dictionary<int, IReadOnlyList<PersonalSwim>> { [7] = swims };

    [Fact]
    public void PersonalOutlier_TwiceOwnBest_Flagged()
    {
        var rows = new[] { PointRow(1, 678) };
        var history = History(
            new PersonalSwim(2, 312, Day1.AddDays(-8)),
            new PersonalSwim(3, 290, Day1.AddDays(-30)),
            new PersonalSwim(4, 275, Day1.AddDays(-60)));

        var v = Assert.Single(Detect(rows, history));
        Assert.Equal(SuspectReasons.PersonalOutlier, v.Reason);
        Assert.Contains("312", v.Note);
    }

    [Fact]
    public void PersonalOutlier_ConfirmedByOwnHeat_NotFlagged()
    {
        // Живой случай (Maccabiah 2026, competition 1485): мастерс 1977 г.р. проплыл
        // 50 вольным за 31.95 (256 очков) при своих же 50 баттерфляем 41.06 (82),
        // 100 вольным 1:17.46 (74) и 50 на спине 47.75 (76) — формально «втрое выше
        // собственного уровня», фактически обычный спринтер. Протокол это подтверждает:
        // он выиграл СВОЙ заплыв у соседей 32.84 / 34.47 / 35.17, то есть время видели судьи.
        var rows = new List<SuspectCandidateRow>
        {
            HeatRow(1, 31_950, heat: 2, swimmerId: 7, points: 256),
            HeatRow(2, 32_840, heat: 2, swimmerId: 21),
            HeatRow(3, 34_470, heat: 2, swimmerId: 22),
            HeatRow(4, 35_170, heat: 2, swimmerId: 23),
        };
        var history = History(
            new PersonalSwim(20, 82, Day1.AddDays(-1)),
            new PersonalSwim(21, 74, Day1.AddDays(-2)),
            new PersonalSwim(22, 76, Day1.AddDays(-3)));

        Assert.Empty(Detect(rows, history));
    }

    [Fact]
    public void PersonalOutlier_IsolatedInOwnHeat_StillFlagged()
    {
        // Ошибка протокола изолирована и в заплыве: 01:53.09 на 200 вольным у 13-летнего
        // (competition 1527) стоит при ближайшем соседе 02:28.82 — 0.76 от него.
        // Страховка «согласовано со своим заплывом» такую строку не спасает.
        var rows = new List<SuspectCandidateRow>
        {
            HeatRow(1, 113_090, heat: 1, swimmerId: 7, points: 678, distance: "200"),
            HeatRow(2, 148_820, heat: 1, swimmerId: 21, distance: "200"),
            HeatRow(3, 151_410, heat: 1, swimmerId: 22, distance: "200"),
            HeatRow(4, 153_910, heat: 1, swimmerId: 23, distance: "200"),
        };
        var history = History(
            new PersonalSwim(20, 312, Day1.AddDays(-8)),
            new PersonalSwim(21, 290, Day1.AddDays(-30)),
            new PersonalSwim(22, 275, Day1.AddDays(-60)));

        var v = Assert.Single(Detect(rows, history));
        Assert.Equal(SuspectReasons.PersonalOutlier, v.Reason);
    }

    [Fact]
    public void PersonalOutlier_WithinOwnLevel_NotFlagged()
    {
        var rows = new[] { PointRow(1, 400) };
        var history = History(
            new PersonalSwim(2, 312, Day1.AddDays(-8)),
            new PersonalSwim(3, 290, Day1.AddDays(-30)),
            new PersonalSwim(4, 275, Day1.AddDays(-60)));

        Assert.Empty(Detect(rows, history));
    }

    [Fact]
    public void PersonalOutlier_OldSwimsOnly_NotFlagged()
    {
        // Подросток за год легально прибавляет 10–15%: сравнение с прошлогодним результатом
        // ловило бы рост, а не ошибку. Поэтому окно ±120 дней.
        var rows = new[] { PointRow(1, 678) };
        var history = History(
            new PersonalSwim(2, 312, Day1.AddDays(-400)),
            new PersonalSwim(3, 290, Day1.AddDays(-420)),
            new PersonalSwim(4, 275, Day1.AddDays(-450)));

        Assert.Empty(Detect(rows, history));
    }

    [Fact]
    public void PersonalOutlier_TooFewSwims_NotFlagged()
    {
        // По одному-двум стартам профиля нет: у новичка первый же удачный заплыв дал бы
        // кратный выброс. Ровно так правило превратилось бы в крикуна.
        var rows = new[] { PointRow(1, 678) };
        var history = History(
            new PersonalSwim(2, 312, Day1.AddDays(-8)),
            new PersonalSwim(3, 290, Day1.AddDays(-30)));

        Assert.Empty(Detect(rows, history));
    }

    [Fact]
    public void PersonalOutlier_NoHistory_RuleSilent_OthersStillWork()
    {
        // Истории нет — правило молчит, но остальные проверки обязаны работать как раньше.
        var rows = new[] { Row(1, 32_590) }.Concat(Field(10, 57_330, 65_690, 66_690, 67_020)).ToList();

        var v = Assert.Single(Detect(rows, null), x => x.ResultId == 1);
        Assert.Equal(SuspectReasons.TimeVsDistance, v.Reason);
    }

    /* ── Ось бассейна (И-13) ──────────────────────────────────────────────────────
     * В 25-метровом бассейне вдвое больше поворотов, и времена короткой воды на 1.5–4%
     * быстрее — рекордов на каждую дистанцию ДВА. Пока порог был один (в основном длинная
     * вода), зимний чемпионат в 25 м получил две ложные пометки: 23.46 на 50 на спине
     * против рекорда 50 м 23.55, при том что рекорд 25 м — 22.11.
     */

    [Fact]
    public void ShortCourseSwim_ComparedWithShortCourseRecord()
    {
        // Живой случай: תומר שוסטר, 50 на спине, 23.46 в 25-метровом бассейне. До рекорда
        // короткой воды (22.11) ему 1.35 с — обычный сильный результат.
        var shortCourse = new[] { Row(1, 23_460, "backstroke", "50", "male", pool: "25m") };
        Assert.Empty(Detect(shortCourse));

        // То же время в ДЛИННОЙ воде было бы быстрее мирового рекорда 23.55 — и вот там
        // пометка законна.
        var longCourse = new[] { Row(1, 23_460, "backstroke", "50", "male", pool: "50m") };
        var v = Assert.Single(Detect(longCourse));
        Assert.Equal(SuspectReasons.TimeVsDistance, v.Reason);
        Assert.Contains("50 м (23.55)", v.Note);
    }

    [Fact]
    public void PoolIsNamedInTheNote()
    {
        // Пометка обязана говорить, с чем сверялись: иначе спор «это же не быстрее рекорда»
        // не разрешить, не читая код (ровно так и вскрылась И-13).
        var rows = new[] { Row(1, 53_000, "backstroke", "100", "female", pool: "25m") };
        var v = Assert.Single(Detect(rows));
        Assert.Contains("мирового рекорда 25 м (54.02)", v.Note);
    }

    [Fact]
    public void NoRecordForOwnPool_FallsBackToShortCourse_AndSaysSo()
    {
        // 100 к/п в длинной воде не плавают, рекорда для неё в справочнике нет. Решение
        // Влада: мерить по короткой воде, но предупреждать — молчать хуже, чем сверить
        // по заведомо МЯГКОМУ порогу и сказать об этом.
        var rows = new[] { Row(1, 48_000, "individual_medley", "100", "male", pool: "50m") };
        var v = Assert.Single(Detect(rows));
        Assert.Equal(SuspectReasons.TimeVsDistance, v.Reason);
        Assert.Contains("(49.28)", v.Note);
        Assert.Contains("рекорда для 50 м в справочнике нет, сверено по рекорду 25 м", v.Note);
    }

    [Fact]
    public void UnknownPool_UsesShortCourse_AndSaysSo()
    {
        // Бассейн не заполнен — берём короткую воду (порог мягче) и признаёмся в этом.
        var rows = new[] { Row(1, 21_000, "backstroke", "50", "male", pool: null) };
        var v = Assert.Single(Detect(rows));
        Assert.Contains("(22.11)", v.Note);
        Assert.Contains("бассейн соревнования неизвестен", v.Note);
    }

    [Fact]
    public void WithoutReference_WorldRecordRulesStaySilent()
    {
        // Справочник не подан — обвинять «быстрее рекорда», не зная рекорда, нельзя.
        // Остальные правила при этом живут.
        var rows = new[] { Row(1, 5_000, "backstroke", "50", "male") };
        Assert.Empty(SuspectResultDetector.Detect(rows));
    }

    [Fact]
    public void OneRowGetsOneReason()
    {
        // 32.59 на сотне подходит и под «быстрее МР», и под «время отрезка», и под выброс —
        // причина должна быть ровно одна, иначе пометка не отвечает на «почему».
        var rows = new[] { Row(1, 32_590) }.Concat(Field(10, 53_420, 57_330, 65_690, 66_690)).ToList();
        var all = Detect(rows).Where(v => v.ResultId == 1).ToList();
        Assert.Single(all);
    }
}
