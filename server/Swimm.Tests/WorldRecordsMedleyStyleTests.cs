using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.WorldRecords;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// World Aquatics печатает комплекс БЕЗ слова Individual: колонка Event у отчёта
/// (api.worldaquatics.com/fina/records/report) содержит «Women's 200m Medley», а не
/// «Women's 200m Individual Medley». Пока парсер оставлял такой стиль как «medley», импорт
/// заводил ПАРАЛЛЕЛЬНЫЙ набор из 32 строк, невидимый всему, что ищет по каноническому
/// individual_medley: рекорды комплекса обновлялись мимо справочника, а строки сидера
/// оставались протухшими — у женских 200 и 400 к/п длинной воды с ПУСТЫМ временем
/// (docs/data-integrity.md, И-13).
///
/// Канон один и тот же во всех входах: Styles, JsonImportService.NormalizeStyleName,
/// HebrewTextHelper.NormalizeStyleName — везде individual_medley.
/// </summary>
public class WorldRecordsMedleyStyleTests
{
    /// <summary>Отчёт worldaquatics в миниатюре: те же колонки и те же строки Event.</summary>
    private static MemoryStream Report(params (string Event, string Time, string Athlete)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Records");
        string[] headers =
        [
            "Event", "Pool", "Record Description", "Record", "Time", "Athlete", "NF Code",
            "Gender", "Competition", "Country", "City", "Date", "Splits", "Tags", "Status"
        ];
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];

        for (var i = 0; i < rows.Length; i++)
        {
            var r = ws.Row(i + 2);
            var women = rows[i].Event.StartsWith("Women", System.StringComparison.Ordinal);
            r.Cell(1).Value = rows[i].Event;
            r.Cell(2).Value = "LCM";
            r.Cell(3).Value = "Meet Record";
            r.Cell(4).Value = "WR";
            r.Cell(5).Value = rows[i].Time;
            r.Cell(6).Value = rows[i].Athlete;
            r.Cell(7).Value = "CAN";
            r.Cell(8).Value = women ? "W" : "M";
            r.Cell(12).Value = "2025-06-09";
            r.Cell(15).Value = "Approved";
        }

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void MedleyWithoutTheWordIndividual_IsCanonicalIndividualMedley()
    {
        // Реальные строки отчёта WR_LCM за 2026-08-24.
        using var primary = Report(
            ("Women's 200m Medley", "02:05.70", "MCINTOSH Summer"),
            ("Women's 400m Medley", "04:23.65", "MCINTOSH Summer"),
            ("Men's 200m Medley", "01:52.69", "MARCHAND Leon"));
        using var secondary = Report(("Women's 50m Freestyle", "23.55", "WALSH Gretchen"));

        var results = new WorldRecordsParser()
            .Parse(new ParseRequest(primary, "WR_SCM.xlsx", secondary, "WR_LCM.xlsx"))
            .ToList();

        var medley = results.Where(r => r.EventStyleName.Contains("medley")).ToList();
        Assert.Equal(3, medley.Count);
        Assert.All(medley, r => Assert.Equal("individual_medley", r.EventStyleName));
        Assert.DoesNotContain(results, r => r.EventStyleName == "medley");

        // Времена доезжают: ради них всё и затевалось — у женских 200/400 к/п длинной воды
        // в справочнике стояла пустота.
        var w200 = Assert.Single(medley, r => r.EventStyleLen == "200" && r.EventStyleGender == "female");
        Assert.Equal("02:05.70", w200.Time);
    }

    [Fact]
    public void MedleyRelay_AlsoLandsOnTheCanonicalStyle()
    {
        // «4x100m Medley Relay»: слово Relay отрезается раньше, остаётся тот же «Medley».
        // В протоколах комплексная эстафета лежит как individual_medley + дистанция 4X100.
        using var primary = Report(("Women's 4x100m Medley Relay", "03:49.34", "HUSKE Torri, SMITH Regan"));
        using var secondary = Report(("Women's 50m Freestyle", "23.55", "WALSH Gretchen"));

        var relay = Assert.Single(
            new WorldRecordsParser().Parse(new ParseRequest(primary, "WR_SCM.xlsx", secondary, "WR_LCM.xlsx")).ToList(),
            r => r.EventStyleName.Contains("medley"));

        Assert.Equal("individual_medley", relay.EventStyleName);
        Assert.True(relay.IsRelay);
    }
}
