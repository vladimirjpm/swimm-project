using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// И-39: экспорт loglig напечатал заплыв каждой строкой дважды — comp 1658
/// («תחרות ליגה פתוחה 2 מכבי חיפה», 07/03/2025, loglig 11898), женский 200 вольным, места
/// 1,1,3,3,5,5,… Импорт завёл 7 точных дублей (И10), /Admin/Health поднял 7 ошибок
/// `results.exact-duplicate` и `results.merged-sessions`. Парсер теперь пропускает вторую
/// копию строки, идущую подряд, и пишет это в журнал разбора (он виден в превью).
/// Фикстура — страница 7 настоящего протокола.
/// </summary>
public class IsrOrgExactDuplicateRowTests
{
    private static string PdfPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "OpenLeague2Haifa2025_1658_doubled_rows_HE.pdf");

    [Fact]
    public void RealPdf_RowPrintedTwice_IsTakenOnce()
    {
        using var fs = File.OpenRead(PdfPath);
        var comp = Assert.Single(IsrOrgCompetitionParser.ParseCompetitions(fs, "he"));

        Assert.Equal("200", comp.EventStyleLen);
        Assert.Equal("female", comp.EventStyleGender);
        Assert.Equal(7, comp.Results.Count);
        Assert.Equal(7, comp.Results.Select(r => r.LastName).Distinct().Count());
        // Места — как напечатаны (Р76): протокол пронумеровал задвоенный список, мы их не правим.
        Assert.Equal([1, 3, 5, 7, 9, 11], comp.Results.Where(r => r.Position != null).Select(r => (int)r.Position!));

        var log = IsrOrgCompetitionParser.GetDebugLog();
        Assert.Equal(7, log.Split('\n').Count(l => l.Contains("WARN exact duplicate row skipped")));
    }
}
