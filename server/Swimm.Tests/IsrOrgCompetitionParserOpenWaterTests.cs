using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регрессия по реальному протоколу «אליפות ישראל- מים פתוחים- אילת 2026» (OrgCompId 16809,
/// 25–27.04.2026): открытая вода печатает дистанцию в КИЛОМЕТРАХ — «5K חופשי», «10K חופשי»,
/// вперемешку с метровыми «1600 חופשי» и «3000 חופשי נוקאוט».
///
/// Заголовки ждали цифры (<c>^\d+</c>), поэтому километровые строки заголовками не считались.
/// Последствия были тихие и разные:
///  • блок 5K шёл ПЕРВЫМ в файле — до него ни одного события не открылось, и все 73 строки
///    пропали молча;
///  • блок 10K шёл после 3000-нокаута и дописался В НЕГО — в одном «заплыве» оказались
///    времена от 5 минут до двух часов, а проверка качества честно ругалась на «повтор
///    дисциплины за день» у Йонатана Ахдута (05:38.05 и 1:53:56.96).
/// </summary>
public class IsrOrgCompetitionParserOpenWaterTests
{
    private static string PdfPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "isr-open-water-2026.pdf");

    [Fact]
    public void KilometreHeaders_BecomeTheirOwnEventsInMetres()
    {
        Assert.True(File.Exists(PdfPath), $"Фикстура протокола не найдена: {PdfPath}");

        using var fs = File.OpenRead(PdfPath);
        var comps = IsrOrgCompetitionParser.ParseCompetitions(fs, "he").ToList();

        // Дистанции только в метрах: «5K» и «10K» наружу не проходят.
        var lens = comps.Select(c => c.EventStyleLen).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(["10000", "1600", "3000", "4X1500", "5000"], lens);

        // 5K — первый блок файла, раньше терялся целиком.
        Assert.Equal(73, comps.Where(c => c.EventStyleLen == "5000").Sum(c => c.Results.Count));
        Assert.Equal(21, comps.Where(c => c.EventStyleLen == "10000").Sum(c => c.Results.Count));
        // 238 личных + 17 команд микс-эстафеты 4X1500.
        Assert.Equal(255, comps.Sum(c => c.Results.Count));

        // Нокаут-3000 — это заплывы на 5–7 минут; двухчасовых времён (10K) там больше нет.
        var knockout = comps.Where(c => c.EventStyleLen == "3000").SelectMany(c => c.Results).ToList();
        Assert.NotEmpty(knockout);
        Assert.All(knockout, r => Assert.True(
            string.IsNullOrEmpty(r.Time) || r.Time!.StartsWith("0:0"),
            $"в 3000-нокауте оказалось время {r.Time} — снова слиплись события"));

        // Эталон инцидента: у Ахдута два заплыва разных дистанций, а не «повтор дисциплины».
        var ahdut = comps
            .SelectMany(c => c.Results.Select(r => (c.EventStyleLen, r.Time, r.LastName)))
            .Where(x => x.LastName.Contains("אחדות") && x.Time is "0:05:38.05" or "1:53:56.96")
            .ToList();
        Assert.Equal(2, ahdut.Count);
        Assert.Equal(2, ahdut.Select(x => x.EventStyleLen).Distinct().Count());
    }

    /// <summary>
    /// Шапка микс-эстафеты открытой воды — «4X1500 שליחים מיקס»: без слова стиля и с миксом
    /// ПОСЛЕ «эстафеты». Её не узнавала ни одна регулярка, и все 17 команд (68 участников)
    /// пропадали молча — в БД у #1547 эстафеты не было вовсе (найдено 07.10.2026).
    /// </summary>
    [Fact]
    public void MixedRelayHeaderWithoutStyle_GivesAllSeventeenTeams()
    {
        using var fs = File.OpenRead(PdfPath);
        var relay = Assert.Single(IsrOrgCompetitionParser.ParseCompetitions(fs, "he"),
            c => c.EventStyleLen == "4X1500");

        Assert.Equal("freestyle", relay.EventStyleName);
        Assert.Equal("none", relay.EventStyleGender);       // смешанную ставит импорт по составу
        Assert.Equal(17, relay.Results.Count);
        Assert.All(relay.Results, r => Assert.Equal(4, r.RelaySwimmers?.Count));

        var first = relay.Results[0];
        Assert.Equal((1, "הפועל בת ים", "1:30:08.17"), (first.Position, first.RelayTeamName, first.Time));

        // Снятая команда — без времени, со статусом.
        var dq = Assert.Single(relay.Results, r => r.TimeFailNote == "DQ");
        Assert.Equal(("הפועל עמק חפר", 17), (dq.RelayTeamName, dq.Lane));
    }

    // ── Эйлат-2025 (compID 6601, loglig 12027): та же беда в АНГЛИЙСКОЙ версии ─────────────

    private static string Eilat2025(string lang) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", $"OpenWaterEilat2025_6601_K_headers_{lang}.pdf");

    /// <summary>
    /// Английский заголовок «5K Freestyle - Men 14-99» не узнавался (<c>HeaderRxEN</c> ждал
    /// только метры): EN-разбор терял все четыре события 5K/10K, события двух языков
    /// разъезжались по номеру, и склейка пары падала на «Results diverged in '1600m
    /// Freestyle - Men 12-99'» — весь старт шёл одним ивритом, без EN-имён (10.10.2026).
    /// </summary>
    [Fact]
    public void EnglishKilometreHeaders_AreEvents()
    {
        using var fs = File.OpenRead(Eilat2025("EN"));
        var comps = IsrOrgCompetitionParser.ParseCompetitions(fs, "en").ToList();

        Assert.Equal(["5000", "5000", "1600", "1600", "10000", "10000"], comps.Select(c => c.EventStyleLen));
    }

    /// <summary>
    /// Дата заплыва в английской версии — ПЕРВОЕ слово строки под заголовком
    /// («06/03/2025 09:00»), в ивритской — второе. Брали второе, английская ветка падала в
    /// запасную дату, а склейка пары берёт дату именно из неё: три дня Эйлата слипались в два.
    /// </summary>
    [Fact]
    public void RealPair_GluesWithEnglishNamesAndDaysOfTheProtocol()
    {
        using var he = File.OpenRead(Eilat2025("HE"));
        using var en = File.OpenRead(Eilat2025("EN"));
        var rows = new IsrOrgParser()
            .Parse(new ParseRequest(he, "x-he.pdf", en, "x-en.pdf", Language: "he"))
            .ToList();

        Assert.Equal(185, rows.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.LastNameEn) || r.LastNameEn == r.LastName,
            $"нет EN-имени у {r.LastName} {r.FirstName}"));

        var days = rows.GroupBy(r => r.EventStyleLen)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Date).Distinct().OrderBy(d => d).ToList());
        Assert.Equal(["06/03/2025"], days["5000"]);
        Assert.Equal(["06/03/2025"], days["1600"]);
        Assert.Equal(["08/03/2025", "11/03/2025"], days["10000"]);   // мужчины 8-го, женщины 11-го

        var arad = Assert.Single(rows, r => r.LastName == "ארד" && r.FirstName == "איתי" && r.EventStyleLen == "1600");
        Assert.Equal(("Arad", "Itay", "0:21:00.00"), (arad.LastNameEn, arad.FirstNameEn, arad.Time));
    }

    [Theory]
    [InlineData("5K", "5000")]
    [InlineData("10k", "10000")]
    [InlineData("1600", "1600")]
    [InlineData("50m", "50")]
    public void NormalizeHeaderLen_TranslatesKilometres(string raw, string expected) =>
        Assert.Equal(expected, IsrOrgCompetitionParser.NormalizeHeaderLen(raw));
}
