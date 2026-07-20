using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регрессия по реальному файлу "Maccabiah-2026_IL_EN.pdf": в HE/default-режиме
/// парсер обязан находить и индивидуальные, и интернациональные эстафетные
/// (международные, английский текст внутри HE-экспорта) заплывы. Раньше
/// заголовок эстафеты вида "4X100m Freestyle Relay - U17 Girls" (пол/возраст
/// в той же строке) не распознавался в isHE-ветке вообще — 64 эстафетные
/// строки пропадали целиком (см. IsrOrgCompetitionParser.cs, блок `if (isHE)`
/// и добавленный туда матч RelayHeaderEnFull-in-HE).
///
/// Файл не входит в репозиторий (личный download пользователя) — тест
/// пропускается, если его нет на диске, чтобы не ломать CI на других машинах.
/// </summary>
public class IsrOrgCompetitionParserMaccabiahRealPdfTests
{
    private const string PdfPath = @"C:\Users\Vlad\Downloads\Maccabiah-2026_IL_EN.pdf";

    [Fact]
    public void HeMode_ParsesBothIndividualAndInternationalRelayRows()
    {
        if (!File.Exists(PdfPath))
        {
            return; // файл недоступен на этой машине — пропускаем, не проваливаем сборку.
        }

        using var fs = File.OpenRead(PdfPath);
        var results = IsrOrgCompetitionParser.ParseCompetitions(fs, "he").ToList();
        var allResults = results.SelectMany(c => c.Results).ToList();

        var relay = allResults.Where(r => r.IsRelay == true).ToList();
        var indiv = allResults.Where(r => r.IsRelay != true).ToList();

        Assert.Equal(851, indiv.Count);
        Assert.Equal(64, relay.Count);
        Assert.Equal(915, allResults.Count);

        // Реконструкция имён по X-колонкам Last/First должна восстанавливать
        // состав минимум для 52 из 64 эстафетных строк (некоторые — законно
        // null, если формат строки неоднозначен).
        var withNames = relay.Count(r => !string.IsNullOrEmpty(r.RelaySwimmersName));
        Assert.True(withNames >= 52, $"Expected at least 52/64 relay rows with reconstructed names, got {withNames}");
    }
}
