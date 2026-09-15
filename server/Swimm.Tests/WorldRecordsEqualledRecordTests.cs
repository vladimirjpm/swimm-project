using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.WorldRecords;
using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Колонка Record в отчёте World Aquatics — не всегда одно слово. В отчёте WR повторённый
/// (equalled) мировой рекорд помечен «=WR», в отчёте NR — составные «NR, WR», «NR, AM».
/// Пока парсер сравнивал строго с "WR", «=WR» уезжал в национальные: из world пропадал
/// соавтор рекорда (100 брасс ж 25 м — Atkinson рядом с Meilutyte), а в Records оседали
/// одиночки country/USA/open и country/JAM/open (docs/data-integrity.md, И-19).
/// </summary>
public class WorldRecordsEqualledRecordTests
{
    private sealed record Row(string Event, string Record, string Time, string Athlete, string NfCode, string Date);

    /// <summary>Отчёт worldaquatics в миниатюре: те же колонки.</summary>
    private static MemoryStream Report(string pool, params Row[] rows)
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
            r.Cell(1).Value = rows[i].Event;
            r.Cell(2).Value = pool;
            r.Cell(4).Value = rows[i].Record;
            r.Cell(5).Value = rows[i].Time;
            r.Cell(6).Value = rows[i].Athlete;
            r.Cell(7).Value = rows[i].NfCode;
            r.Cell(8).Value = rows[i].Event.StartsWith("Women", System.StringComparison.Ordinal) ? "W" : "M";
            // Колонка Country — страна МЕСТА соревнования, не спортсмена.
            r.Cell(10).Value = "Great Britain";
            r.Cell(12).Value = rows[i].Date;
            r.Cell(15).Value = "Approved";
        }

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    // Реальные строки отчёта WR SCM за 2026-09-15.
    private static readonly Row[] WrScm =
    [
        new("Women's 100m Breaststroke", "WR", "01:02.36", "MEILUTYTE Ruta", "LTU", "2013-10-12"),
        new("Women's 100m Breaststroke", "=WR", "01:02.36", "ATKINSON Alia", "JAM", "2014-12-06"),
        new("Women's 100m Breaststroke", "=WR", "01:02.36", "ATKINSON Alia", "JAM", "2016-08-26"),
        new("Women's 100m Backstroke", "WR", "54.02", "SMITH Regan", "USA", "2024-12-15"),
        new("Women's 100m Backstroke", "=WR", "54.02", "SMITH Regan", "USA", "2025-10-18"),
    ];

    private static readonly Row[] WrLcm =
    [
        new("Women's 50m Freestyle", "WR", "23.61", "SJOESTROEM Sarah", "SWE", "2023-07-29"),
    ];

    [Fact]
    public void EqualledWorldRecord_StaysWorld()
    {
        using var primary = Report("SCM", WrScm);
        using var secondary = Report("LCM", WrLcm);

        var results = new WorldRecordsParser()
            .Parse(new ParseRequest(primary, "WR_SCM.xlsx", secondary, "WR_LCM.xlsx"))
            .ToList();

        Assert.Equal(6, results.Count);
        Assert.All(results, r => Assert.Equal("WR", r.Note));
    }

    [Fact]
    public void CompositeCodesInNationalReport_StayNational()
    {
        // Реальные строки отчёта NR SCM (USA) за 2026-09-15: первым идёт тип, по которому отчёт
        // запрошен, остальное — попутные титулы. «NR, WR» — национальный рекорд, который заодно
        // мировой, и место ему в country/USA, а не в world.
        using var primary = Report("LCM", WrLcm);
        using var secondary = Report("LCM", WrLcm);
        using var nr = Report("SCM",
            new Row("Women's 50m Freestyle", "NR, WR", "22.83", "WALSH Gretchen", "USA", "2024-12-15"),
            new Row("Women's 800m Freestyle", "NR, AM", "07:57.42", "LEDECKY Katie", "USA", "2022-11-05"),
            new Row("Women's 400m Freestyle", "NR", "03:52.88", "LEDECKY Katie", "USA", "2022-10-28"),
            new Row("Men's 50m Freestyle", "NR, MR", "20.16", "DRESSEL Caeleb", "USA", "2020-11-21"),
            // «=NR» в выборке не встретился — зеркало «=WR», чтобы знак «=» не менял смысл
            // и у национального рекорда.
            new Row("Women's 400m Freestyle", "=NR", "03:52.88", "LEDECKY Katie", "USA", "2022-10-28"));

        var results = new WorldRecordsParser()
            .Parse(new ParseRequest(primary, "WR_SCM.xlsx", secondary, "WR_LCM.xlsx",
                ExtraStreams: [(nr, "NR_SCM.xlsx")]))
            .ToList();

        var national = results.Skip(2).ToList();
        Assert.Equal(5, national.Count);
        Assert.All(national, r => Assert.Equal("USA", r.Note));
    }

    [Fact]
    public async Task EqualledWorldRecord_GivesJointHolders_AndNoStrayCountryRows()
    {
        // Весь путь до записи в Records: провайдер (ручная загрузка тех же двух WR-файлов —
        // без похода в сеть) + схлопывание по 8 осям, как делает дифф перед Apply.
        using var primary = Report("SCM", WrScm);
        using var secondary = Report("LCM", WrLcm);
        var provider = new WorldRecordsSourceProvider(new NoNetworkHttpClientFactory(), new WorldRecordsParser());

        var parsed = RecordDiffService.DeduplicateByAxes(await provider.FetchAsync(
            new RecordSourceRequest("worldrecords", primary, "WR_SCM.xlsx", secondary, "WR_LCM.xlsx")));

        Assert.DoesNotContain(parsed, p => p.RegionType == "country");

        var breast = Assert.Single(parsed, p => p.Style == "breaststroke");
        Assert.Equal("world", breast.RegionType);
        Assert.Equal("Ruta Meilutyte, Alia Atkinson", breast.HolderName);
        // Дата и страна — от первой из равных строк, то есть от того, кто установил рекорд.
        Assert.Equal("12/10/2013", breast.RecordDate);
        Assert.Equal("LTU", breast.HolderCountry);

        // Повторил сам себя — держатель один, имя не дублируется.
        var back = Assert.Single(parsed, p => p.Style == "backstroke");
        Assert.Equal("Regan Smith", back.HolderName);
        Assert.Equal("15/12/2024", back.RecordDate);
    }

    /// <summary>Ручная загрузка в сеть не ходит — любой запрос здесь означает поломку теста.</summary>
    private sealed class NoNetworkHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new System.InvalidOperationException("Ручная загрузка не должна ходить в сеть.");
    }
}
