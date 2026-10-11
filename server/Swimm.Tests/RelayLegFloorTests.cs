using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Предел этапа эстафеты по рекорду Израиля вольным для возраста (решение Влада 10.10.2026,
/// docs/relays.md «Невозможный этап»): сбой касания loglig печатает этапы вроде 00:18.06 у
/// одиннадцатилетних, и ни один сторож разборщика их не видит — нарастающие ровные.
/// </summary>
public class RelayLegFloorTests
{
    // Реальные значения справочника (бассейн 25 м, мужчины): 50 м — 14 лет 23.59, 15 — 22.80,
    // открытый 21.08; женщины 13 лет — 26.20 (условно).
    private static readonly RelayLegFloor Floor = new(
    [
        new FreestyleRecordRow(10, "male", "25m", 50, 29_810),
        new FreestyleRecordRow(14, "male", "25m", 50, 23_590),
        new FreestyleRecordRow(15, "male", "25m", 50, 22_800),
        new FreestyleRecordRow(18, "male", "25m", 50, 21_680),
        new FreestyleRecordRow(null, "male", "25m", 50, 21_080),
        new FreestyleRecordRow(13, "female", "25m", 50, 26_200),
        new FreestyleRecordRow(null, "female", "25m", 50, 24_000),
        new FreestyleRecordRow(14, "male", "25m", 100, 51_590),
    ]);

    [Fact]
    public void KidLeg_FasterThanAgeRecord_IsImplausible()
    {
        // Команда 3807 (1527, 01.02.2025): мальчик 2011 г. р., этап 00:19.67 при соседних 27 с.
        Assert.NotNull(Floor.Implausible("00:19.67", "4X50", "male", "25m", 2011, 2025));
    }

    [Fact]
    public void AgeIsUpperBound_YearMinusBirthPlusOne()
    {
        // 2025 − 2011 + 1 = 15 → рекорд 15 лет 22.80 → предел 22.80 × 0.97 − 1 = 21.116.
        Assert.Equal(21_116, Floor.FloorMs("male", "25m", 50, 2011, 2025));
    }

    [Fact]
    public void RealisticKidLeg_IsPlausible()
    {
        Assert.Null(Floor.Implausible("00:27.50", "4X50", "male", "25m", 2011, 2025));
    }

    [Fact]
    public void AdultFastLeg_UsesOpenRecord_IsPlausible()
    {
        // Мастерс 1985 г. р.: старше последней ступени → открытый рекорд 21.08 → предел 19.45.
        Assert.Null(Floor.Implausible("00:20.10", "4X50", "male", "25m", 1985, 2026));
        Assert.NotNull(Floor.Implausible("00:00.69", "4X50", "female", "25m", 1983, 2026));
    }

    [Fact]
    public void YoungerThanFirstStep_UsesFirstStep()
    {
        // 9 лет → ступени 9 нет, берётся ближайшая старшая (10, 29.81): она быстрее, предел ниже.
        Assert.Equal(29_810 * 97 / 100 - 1000, Floor.FloorMs("male", "25m", 50, 2017, 2025));
    }

    [Fact]
    public void MissingStep_UsesNearestOlder()
    {
        // 12 лет → ступеней 11–13 у мужчин нет, ближайшая старшая — 14 (23.59).
        Assert.Equal(23_590 * 97 / 100 - 1000, Floor.FloorMs("male", "25m", 50, 2014, 2025));
    }

    [Fact]
    public void UnknownGender_TakesFasterOfBoth()
    {
        Assert.Equal(Floor.FloorMs("male", "25m", 50, 2012, 2025), Floor.FloorMs(null, "25m", 50, 2012, 2025));
    }

    [Fact]
    public void NoRecord_NoFloor_NotChecked()
    {
        Assert.Null(Floor.FloorMs("male", "50m", 50, 2011, 2025));   // длинной воды в справочнике теста нет
        Assert.Null(Floor.Implausible("00:01.00", "4X1500", "male", "25m", 2011, 2025));
        Assert.Null(RelayLegFloor.Empty.Implausible("00:01.00", "4X50", "male", "25m", 2011, 2025));
    }

    [Fact]
    public void LegDistance_ReadsRelayForms()
    {
        Assert.Equal(50, RelayLegFloor.LegDistance("4X50"));
        Assert.Equal(100, RelayLegFloor.LegDistance("4x100m"));
        Assert.Equal(200, RelayLegFloor.LegDistance("4X200"));
        Assert.Null(RelayLegFloor.LegDistance("100"));
    }

    [Fact]
    public void TeamImplausible_ChecksDbLegs()
    {
        var row = new RelayRow(1, 7, "freestyle", "4X50", 2, 8, "01:47.83",
        [
            new RelayLegRow(1, 1, 2012, "00:26.93", "male"),
            new RelayLegRow(2, 2, 2012, "00:27.50", "male"),
            new RelayLegRow(3, 3, 2011, "00:19.67", "male"),
            new RelayLegRow(4, 4, 2011, "00:33.73", "male"),
        ], 2025, "25m");

        Assert.StartsWith("нога 3:", Floor.TeamImplausible(row));
        Assert.Null(Floor.TeamImplausible(row with { Year = null }));
    }
}
