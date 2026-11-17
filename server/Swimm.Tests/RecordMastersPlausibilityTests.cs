using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сторож «мировой улучшен больше чем на 3 %» на мастерсах (17.11.2026). Порог калибровали на
/// взрослых мировых, а в мастерсах скачок почти всегда — сильный пловец перешёл в новую полосу.
/// 17.11 сторож завёл семь ложных кандидатов (Laura Val в 75-79, Heidi George в 50-54, Tsutomu
/// Hayashi в 90-94); все семь — медленнее рекорда младшей полосы. Значения в тестах — живые.
/// </summary>
public class RecordMastersPlausibilityTests
{
    private static RecordPlausibility.WorldRow Band(string ageKey, string gender, string pool,
        string style, string distance, string time) =>
        new("masters", ageKey, gender, pool, style, distance, time);

    private static RecordDiffEntry Changed(string ageKey, string gender, string pool, string style,
        string distance, string oldTime, string newTime, string category = "masters") =>
        new("world", "", category, ageKey, gender, pool, style, distance,
            oldTime, "Old Holder", "01/01/2020", newTime, "New Holder", "09/08/2026");

    private static IReadOnlyList<RecordSuspiciousEntry> Check(
        RecordDiffEntry entry, params RecordPlausibility.WorldRow[] reference) =>
        RecordPlausibility.Check([entry], RecordPlausibility.WorldReference(reference));

    /// <summary>Laura Val, 200 на спине 50 м: 3:09.61 → 2:54.21 (−8.1 %), её же 70-74 — 2:49.43.</summary>
    [Fact]
    public void AgingUp_SlowerThanYoungerBand_IsNotSuspicious()
    {
        var found = Check(
            Changed("75-79", "female", "50m", "backstroke", "200m", "03:09.61", "02:54.21"),
            Band("70-74", "female", "50m", "backstroke", "200m", "02:49.43"),
            Band("75-79", "female", "50m", "backstroke", "200m", "02:54.21"));

        Assert.Empty(found);
    }

    /// <summary>Все семь ложных кандидатов 17.11.2026 с живыми рекордами младших полос.</summary>
    [Theory]
    [InlineData("50-54", "female", "50m", "freestyle", "1500m", "18:15.80", "17:33.76", "45-49", "17:27.11")]
    [InlineData("75-79", "female", "50m", "freestyle", "50m", "32.96", "31.28", "70-74", "30.61")]
    [InlineData("75-79", "female", "50m", "freestyle", "100m", "01:12.77", "01:09.58", "70-74", "01:06.68")]
    [InlineData("75-79", "female", "50m", "backstroke", "50m", "37.08", "35.58", "70-74", "34.65")]
    [InlineData("75-79", "female", "50m", "backstroke", "100m", "01:21.02", "01:18.27", "70-74", "01:16.27")]
    [InlineData("75-79", "female", "50m", "backstroke", "200m", "03:09.61", "02:54.21", "70-74", "02:49.43")]
    [InlineData("90-94", "male", "25m", "freestyle", "50m", "39.93", "38.28", "85-89", "32.23")]
    public void FalseCandidatesOf17November_AreNotSuspicious(string ageKey, string gender, string pool,
        string style, string distance, string oldTime, string newTime, string youngerKey, string youngerTime)
    {
        var found = Check(
            Changed(ageKey, gender, pool, style, distance, oldTime, newTime),
            Band(youngerKey, gender, pool, style, distance, youngerTime));

        Assert.Empty(found);
    }

    /// <summary>Скачок, который переходом не объяснить: стал быстрее младшей полосы — находка.</summary>
    [Fact]
    public void BigJump_FasterThanYoungerBand_IsSuspicious()
    {
        var found = Check(
            Changed("75-79", "female", "50m", "freestyle", "100m", "01:12.77", "01:02.00"),
            Band("70-74", "female", "50m", "freestyle", "100m", "01:06.68"));

        var finding = Assert.Single(found);
        Assert.Equal(RecordIssueReasons.ImplausibleImprovement, finding.Reason);
        Assert.Contains("младшей полосы 70-74", finding.Note);
    }

    /// <summary>Маленькое улучшение быстрее младшей полосы — не находка: в 5 % пар лестница так и стоит.</summary>
    [Fact]
    public void SmallGain_FasterThanYoungerBand_IsNotSuspicious()
    {
        var found = Check(
            Changed("45-49", "female", "25m", "freestyle", "1500m", "17:10.00", "17:01.27"),
            Band("40-44", "female", "25m", "freestyle", "1500m", "17:05.00"));

        Assert.Empty(found);
    }

    /// <summary>У первой полосы (25-29) младшей нет — работает прежний порог 3 %.</summary>
    [Fact]
    public void FirstBand_KeepsThreePercentThreshold()
    {
        var found = Check(Changed("25-29", "male", "50m", "freestyle", "50m", "23.00", "21.50"));

        Assert.Equal(RecordIssueReasons.ImplausibleImprovement, Assert.Single(found).Reason);
    }

    /// <summary>Рекорда младшей полосы в эталоне нет — тоже прежний порог.</summary>
    [Fact]
    public void YoungerBandMissing_KeepsThreePercentThreshold()
    {
        var found = Check(Changed("75-79", "female", "50m", "freestyle", "100m", "01:12.77", "01:09.58"));

        Assert.Single(found);
    }

    /// <summary>Взрослые мировые новая мерка не трогает: там 3 % по-прежнему без исключений.</summary>
    [Fact]
    public void OpenWorld_StillFlaggedBeyondThreePercent()
    {
        var found = Check(Changed("", "female", "50m", "freestyle", "100m", "51.68", "40.11", category: "open"));

        Assert.Equal(RecordIssueReasons.ImplausibleImprovement, Assert.Single(found).Reason);
    }

    [Theory]
    [InlineData("75-79", "70-74")]
    [InlineData("30-34", "25-29")]
    [InlineData("100-104", "95-99")]
    [InlineData(" 50-54 ", "45-49")]
    [InlineData("25-29", null)]
    [InlineData("", null)]
    [InlineData("80+", null)]
    [InlineData("12", null)]
    public void YoungerBand_StepsFiveYearsDown(string ageKey, string? expected) =>
        Assert.Equal(expected, RecordPlausibility.YoungerBand(ageKey));
}
