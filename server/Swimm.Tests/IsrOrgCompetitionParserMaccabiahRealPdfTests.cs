using System;
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
/// Протокол лежит в Fixtures/Parsing — тест в сеть НЕ ходит и не зависит от
/// машины. Раньше путь вёл в личный Downloads, а отсутствие файла молча
/// возвращало зелёный: тест «проходил», ничего не проверив.
/// </summary>
public class IsrOrgCompetitionParserMaccabiahRealPdfTests
{
    private static string PdfPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "Maccabiah-2026_IL_EN.pdf");

    [Fact]
    public void HeMode_ParsesBothIndividualAndInternationalRelayRows()
    {
        Assert.True(File.Exists(PdfPath), $"Фикстура протокола не найдена: {PdfPath}");

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
