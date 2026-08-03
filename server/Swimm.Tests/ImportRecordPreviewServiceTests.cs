using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Сколько рекордов побьёт файл» на превью импорта (docs/data-integrity.md §12, Б2).
///
/// Живой случай, ради которого проверка появилась: 200 вольным за 01:53.09 у 13-летнего.
/// Рекорд ступени 13 — 01:59.85, то есть файл «бьёт» его почти на семь секунд. Настоящий
/// рекорд редок; увидеть такое ДО «Применить» дешевле, чем разбирать инцидент потом.
/// </summary>
public class ImportRecordPreviewServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static Swimm.Domain.Entities.Record AgeRecord(string ageKey, string time, string holder = "גל כהן גרומי") => new()
    {
        RegionType = "country", RegionCode = "ISR", Category = "age", AgeKey = ageKey,
        Gender = "male", PoolType = "25m", Style = "freestyle", Distance = "200m",
        Time = time, HolderName = holder
    };

    private static string Json(string time, int birthYear = 2012, string distance = "200") => $$"""
        [{
          "competition": "מוקדמות אליפות צעירים",
          "date": "01/02/2025",
          "event_style_name": "freestyle",
          "event_style_len": "{{distance}}",
          "event_style_gender": "male",
          "pool_type": "25m",
          "position": 1, "heat": 1, "lane": 4,
          "last_name": "ורדי איתן", "first_name": "אפרים",
          "birth_year": {{birthYear}},
          "club": "מכבי ירושלים",
          "time": "{{time}}"
        }]
        """;

    [Fact]
    public async Task ImpossibleSwim_ShowsUpAsRecordBreak_WithTheRecordItBeats()
    {
        await using var db = CreateDb(nameof(ImpossibleSwim_ShowsUpAsRecordBreak_WithTheRecordItBeats));
        db.Records.Add(AgeRecord("13", "01:59.85"));
        await db.SaveChangesAsync();

        var result = await new ImportRecordPreviewService(db).AnalyzeAsync(Json("01:53.09"));

        Assert.Null(result.Error);
        Assert.Equal(1, result.Count);
        var row = Assert.Single(result.Rows);
        // Старый рекорд обязателен: «побьёт 1 рекорд» без него — цифра, которую нечем проверить.
        Assert.Equal("01:59.85", row.RecordTime);
        Assert.Equal("Age 13 record", row.Kind);
        Assert.Contains("ורדי איתן", row.SwimmerName);
    }

    [Fact]
    public async Task NormalSwim_BreaksNothing()
    {
        await using var db = CreateDb(nameof(NormalSwim_BreaksNothing));
        db.Records.Add(AgeRecord("13", "01:59.85"));
        await db.SaveChangesAsync();

        var result = await new ImportRecordPreviewService(db).AnalyzeAsync(Json("02:14.30"));

        Assert.Null(result.Error);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task EmptyRecordsTable_SaysSoInsteadOfReportingZero()
    {
        // «Сверять не с чем» и «рекордов не побито» — разные вещи; ноль тут успокаивал бы зря.
        await using var db = CreateDb(nameof(EmptyRecordsTable_SaysSoInsteadOfReportingZero));

        var result = await new ImportRecordPreviewService(db).AnalyzeAsync(Json("01:53.09"));

        Assert.NotNull(result.Error);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task BrokenJson_DoesNotThrow()
    {
        // Прибор не имеет права сорвать превью импорта.
        await using var db = CreateDb(nameof(BrokenJson_DoesNotThrow));

        var result = await new ImportRecordPreviewService(db).AnalyzeAsync("{ это не json ");

        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task RelaysAndFailedSwims_AreIgnored()
    {
        // Эстафеты вне скоупа детектора (маппинг стилей неоднозначен), DQ/DNS рекордов не бьют.
        await using var db = CreateDb(nameof(RelaysAndFailedSwims_AreIgnored));
        db.Records.Add(AgeRecord("13", "01:59.85"));
        await db.SaveChangesAsync();

        var json = """
            [{
              "date": "01/02/2025", "event_style_name": "freestyle", "event_style_len": "200",
              "event_style_gender": "male", "pool_type": "25m",
              "last_name": "A", "first_name": "B", "birth_year": 2012, "club": "C",
              "time": "01:40.00", "is_relay": true
            },{
              "date": "01/02/2025", "event_style_name": "freestyle", "event_style_len": "200",
              "event_style_gender": "male", "pool_type": "25m",
              "last_name": "D", "first_name": "E", "birth_year": 2012, "club": "C",
              "time": "01:41.00", "time_fail": true
            }]
            """;

        var result = await new ImportRecordPreviewService(db).AnalyzeAsync(json);

        Assert.Equal(0, result.Count);
    }
}
