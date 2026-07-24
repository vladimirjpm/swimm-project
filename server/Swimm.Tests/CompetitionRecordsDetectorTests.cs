using Swimm.Application.Mapping;
using Xunit;
using Record = Swimm.Domain.Entities.Record;
using Swimm.Domain.Entities;

namespace Swimm.Tests;

/// <summary>Тесты чистого детектора «новых рекордов» соревнования (образец — ClubPointsScoringTests).</summary>
public class CompetitionRecordsDetectorTests
{
    private static Record Rec(string category, string ageKey, string time,
        string style = "backstroke", string distance = "50m", string gender = "male", string pool = "50m") => new()
    {
        RegionType = "country", RegionCode = "ISR",
        Category = category, AgeKey = ageKey,
        Gender = gender, PoolType = pool, Style = style, Distance = distance,
        Time = time, HolderName = "Holder"
    };

    private static RecordCandidateRow Row(int timeMs, int? birthYear = 2016, string time = "00:40.00",
        string style = "backstroke", string distance = "50", string gender = "male", string pool = "50m",
        bool isMasters = false, long resultId = 1) => new(
        resultId, 100, "First", "Last", "Club", style, distance, gender, pool,
        birthYear, new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), timeMs, time, 1, isMasters);

    [Fact]
    public void BeatsAgeRecord_ByComputedAge()
    {
        // 2026 - 2016 = возраст 10; рекорд Age 10 = 45.00, заплыв 40.00 — побит.
        var result = CompetitionRecordsDetector.Detect(
            [Rec("age", "10", "45.00")], [Row(40_000)]);

        var dto = Assert.Single(result);
        Assert.Equal("Age 10 record", dto.Kind);
        Assert.Equal(1, dto.ResultId);
    }

    [Fact]
    public void EqualTime_CountsAsRecord()
    {
        // Семантика клиента isRecordTime: время ≤ рекорда — рекорд.
        var result = CompetitionRecordsDetector.Detect(
            [Rec("age", "10", "40.00")], [Row(40_000)]);
        Assert.Single(result);
    }

    [Fact]
    public void SlowerTime_NoRecord()
    {
        var result = CompetitionRecordsDetector.Detect(
            [Rec("age", "10", "39.00")], [Row(40_000)]);
        Assert.Empty(result);
    }

    [Fact]
    public void AxisMismatch_PoolStyleDistanceGender_NoRecord()
    {
        var records = new[]
        {
            Rec("age", "10", "45.00", pool: "25m"),
            Rec("age", "10", "45.00", style: "freestyle"),
            Rec("age", "10", "45.00", distance: "100m"),
            Rec("age", "10", "45.00", gender: "female"),
        };
        Assert.Empty(CompetitionRecordsDetector.Detect(records, [Row(40_000)]));
    }

    [Fact]
    public void OpenAndNationalKeys_Matched()
    {
        // open (AgeKey="") и национальный ключ (AgeKey="ISR") проверяются независимо от возраста.
        var records = new[] { Rec("open", "", "41.00"), Rec("age", "ISR", "42.00") };
        var result = CompetitionRecordsDetector.Detect(records, [Row(40_000, birthYear: null)]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Kind == "Open record");
        Assert.Contains(result, r => r.Kind == "National record");
    }

    [Fact]
    public void Masters_UsesRangeAgeKey()
    {
        // 2026 - 1998 = 28 → диапазон 25-29; обычный age-ключ не проверяется.
        var records = new[] { Rec("masters", "25-29", "45.00"), Rec("age", "28", "45.00") };
        var result = CompetitionRecordsDetector.Detect(
            records, [Row(40_000, birthYear: 1998, isMasters: true)]);

        var dto = Assert.Single(result);
        Assert.Equal("Masters 25-29", dto.Kind);
    }

    [Fact]
    public void FastestSwimPerAxis_Wins()
    {
        // Два заплыва бьют один рекорд — карточка одна, у более быстрого.
        var rows = new[]
        {
            Row(40_000, resultId: 1),
            Row(39_000, resultId: 2, time: "00:39.00"),
        };
        var result = CompetitionRecordsDetector.Detect([Rec("age", "10", "45.00")], rows);

        var dto = Assert.Single(result);
        Assert.Equal(2, dto.ResultId);
        Assert.Equal("00:39.00", dto.Time);
    }

    [Theory]
    [InlineData("35.64", 35_640)]
    [InlineData("02:45.46", 165_460)]
    [InlineData("1:02:45.46", 3_765_460)]
    public void ParseTimeToMs_Formats(string time, int expectedMs)
        => Assert.Equal(expectedMs, CompetitionRecordsDetector.ParseTimeToMs(time));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("1:2:3:4")]
    public void ParseTimeToMs_Garbage_ReturnsNull(string? time)
        => Assert.Null(CompetitionRecordsDetector.ParseTimeToMs(time));
}
