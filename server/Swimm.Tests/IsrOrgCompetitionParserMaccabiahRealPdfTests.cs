using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        // состав минимум для 62 из 64 эстафетных строк (было 52 до фикса
        // сборки ног эстафеты через разрыв страницы, затем 59 после него — см.
        // ReconstructEnRelaySwimmerNames/ParseLines, комментарии "разрыв
        // страницы" — и ещё +5 после фикса ДВОЙНОГО переноса, когда И фамилия,
        // И имя разорваны на отдельные строки ОДНОВРЕМЕННО, напр.
        // "STRIMO"/"Jonatha 2010"/"VSKY n" -> Jonathan STRIMOVSKY,
        // "ROSEN"/"Frederic 2008"/"THAL k" -> Frederick ROSENTHAL,
        // "SPIEGL"/"Benjami 2008"/"ER n" -> Benjamin SPIEGLER — см.
        // TryFillFromFragmentGroup, которая теперь принимает 2-словные
        // соседние Y-группы как склейку суффиксов ОБЕИХ колонок разом).
        //
        // Оставшиеся 2 строки — НЕ регрессия, а два разных, не входящих в эту
        // задачу случая:
        //  - comp 1484 4X50 "Maccabiah MIX" 02:07.12 — PDF печатает только
        //    ТРИ ноги для этой команды (в источнике нет данных на 4-ю), поэтому
        //    сборка состава ниже currentRelayLegs и результат намеренно null;
        //  - comp 1484 4X50 "Brazil" 01:59.77 — другой баг: ОДНА колонка (Last)
        //    разорвана на ТРИ строки вокруг года ("KOZUC" / "HOWIC Micael 2009"
        //    / "Z" -> должно быть "KOZUCHOWICZ"), при этом First-колонка на
        //    строке года УЖЕ полная ("Micael"). Текущий алгоритм считает
        //    колонку "не пропущенной", раз в ней нашлось хоть какое-то слово
        //    рядом с годом, и не пытается склеивать префикс/суффикс — это
        //    другой класс бага (тройное расщепление ОДНОЙ колонки, а не
        //    одновременный перенос ОБЕИХ), сознательно не в скоупе.
        var withNames = relay.Count(r => !string.IsNullOrEmpty(r.RelaySwimmersName));
        Assert.True(withNames >= 62, $"Expected at least 62/64 relay rows with reconstructed names, got {withNames}");
    }

    /// <summary>
    /// Регрессия: EN-заголовок, где правая часть — категория БЕЗ возраста
    /// ("50m Freestyle - Men", "100m Butterfly - Women", "50m Freestyle - Men Para").
    /// HeaderRxENinHE требует возраст ("U17 Girls"), которого у взрослых и Para в
    /// протоколе нет, поэтому парсер видел лишь 52 события из 91: строки 39 потерянных
    /// заплывов прилипали к предыдущему событию и получали чужие стиль, дистанцию и пол
    /// (женская сотня баттерфляем уезжала в "100m Butterfly - U17 Boys", а Para-заплывы
    /// Itsik Iaich — в "4X100m Medley Relay - Men", где 00:31.93 выглядело эстафетным
    /// временем и порождало ложные рекорды). Разбирает их HeaderEnCategoryInHE
    /// + ParseEnCategory.
    /// </summary>
    [Fact]
    public void HeMode_AdultAndParaHeaders_BecomeTheirOwnEvents()
    {
        using var fs = File.OpenRead(PdfPath);
        var comps = IsrOrgCompetitionParser.ParseCompetitions(fs, "he").ToList();

        Assert.Equal(91, comps.Count);
        Assert.Equal(915, comps.Sum(c => c.Results.Count));

        // Para — отдельные события, не схлопнутые со взрослыми "- Men".
        var para = comps.Where(c => c.Event.EndsWith("Men Para", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(para);
        Assert.All(para, c => Assert.Equal("para", c.EventStyleAge));

        var freePara50 = Assert.Single(comps, c => c.Event == "50m Freestyle - Men Para");
        Assert.Equal("freestyle", freePara50.EventStyleName);
        Assert.Equal("50", freePara50.EventStyleLen);
        Assert.Equal("male", freePara50.EventStyleGender);
        Assert.Contains(freePara50.Results, r => r.Time == "00:31.95" && r.LastName == "Iaich");

        // Женские заплывы взрослых больше не попадают в мужские U17-события.
        var fly100Women = Assert.Single(comps, c => c.Event == "100m Butterfly - Women");
        Assert.Equal("female", fly100Women.EventStyleGender);
        Assert.Contains(fly100Women.Results, r => r.LastName == "MOSHKOVITCH");

        var wrongGender = comps
            .Where(c => c.Event.Contains("Women", StringComparison.OrdinalIgnoreCase)
                        || c.Event.Contains("Girls", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.EventStyleGender != "female")
            .Select(c => c.Event)
            .ToList();
        Assert.True(wrongGender.Count == 0, "Женские события с чужим полом: " + string.Join(", ", wrongGender));
    }

    private static string HePdfPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "Maccabiah-2026_IL_HE.pdf");

    /// <summary>
    /// Регрессия по HEBREW-экспорту ТОГО ЖЕ протокола ("Maccabiah-2026_IL_HE.pdf"):
    /// таблица легов эстафеты в этом файле физически RTL — колонки на странице идут
    /// "год рождения | имя | фамилия" (год слева), а не "фамилия | имя | год", как в
    /// EN-экспорте. Раньше реконструкция колонок опознавала только английские подписи
    /// шапки ("Last"/"First") и предполагала фамилию крайней слева — на HE-файле это
    /// давало корректные счётчики строк (915/851/64), но 57 из 64 ростеров эстафет были
    /// испорчены: год рождения утекал в поле имени вместо фамилии (напр. "2009 Hilla"
    /// вместо "Hilla LERNER" — год ПОДМЕНЯЛ фамилию как "первое слово" строки).
    /// Фикс детектирует ивритские подписи шапки ("יטרפ"/"החפשמ") и строит роль→X
    /// маппинг по фактической шапке таблицы, а не по предположению "фамилия слева".
    /// </summary>
    [Fact]
    public void HeMode_RelayRosters_MatchCountAndNeverLeakBirthYearAsName()
    {
        Assert.True(File.Exists(HePdfPath), $"Фикстура HE-протокола не найдена: {HePdfPath}");

        using var fs = File.OpenRead(HePdfPath);
        var results = IsrOrgCompetitionParser.ParseCompetitions(fs, "he").ToList();
        var allResults = results.SelectMany(c => c.Results).ToList();

        var relay = allResults.Where(r => r.IsRelay == true).ToList();
        var indiv = allResults.Where(r => r.IsRelay != true).ToList();

        Assert.Equal(851, indiv.Count);
        Assert.Equal(64, relay.Count);
        Assert.Equal(915, allResults.Count);

        // Главная сигнатура регрессии: 4-значный год рождения, просочившийся в состав
        // как будто это имя пловца. Если роль-маппинг колонок сломан, это выглядит
        // именно так ("2009 Hilla" вместо "Hilla LERNER") — проверяем явно, а не
        // полагаемся только на общий процент воссозданных ростеров.
        foreach (var r in relay)
        {
            if (string.IsNullOrEmpty(r.RelaySwimmersName)) continue;
            Assert.False(
                Regex.IsMatch(r.RelaySwimmersName, @"\b\d{4}\b"),
                $"Relay roster leaked a birth year as a name token: '{r.RelaySwimmersName}'");
        }

        var withNames = relay.Count(r => !string.IsNullOrEmpty(r.RelaySwimmersName));
        Assert.Equal(64, withNames);
    }
}
