using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Вторая сетка эстафетных полос Маккаби — «נוער ובוגרים» (OrgCompId 16818, loglig 14669,
/// comp 1576, 06.06.2026). Протокол печатает эстафеты так же сквозь всю дисциплину
/// («4X50 חופשי שליחים», 40 команд, места по времени вперемешку), но зачётные полосы у
/// старшей половины чемпионата ДРУГИЕ: בנות 14-15, בנים 15-16, נשים 16-99, גברים 17-99 —
/// две младшие узкие, две старшие открыты сверху.
///
/// До фикса <see cref="RelayBandReconstructor"/> знал только сетку «צעירים» (девочки 9-11,
/// 12-13; мальчики 9-10, 11-12, 13-14) и на возрастах 15+ отдавал null → срабатывала
/// страховка «всё или ничего», дисциплина оставалась сквозной, и очки эстафет получали
/// только 20 самых быстрых команд вообще (взрослые парни). Клубный зачёт 1576 давал 908
/// очков эстафет против официальных 2672 — минус 1764 очка, сверка с דירוג מועדונים
/// loglig 2026-08-18.
/// </summary>
public class IsrOrgCompetitionParserMaccabiSeniorRelayTests
{
    private static string PdfPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "isr-maccabi-spring-2026-senior-he.pdf");

    [Fact]
    public void SeniorRelays_AreSplitIntoOpenEndedBands()
    {
        Assert.True(File.Exists(PdfPath), $"Фикстура протокола не найдена: {PdfPath}");

        using var fs = File.OpenRead(PdfPath);
        var results = IsrOrgParser.MapHebrewOnly(
                IsrOrgCompetitionParser.ParseCompetitions(fs, "he"),
                country: "IL", isMastersFile: false, isAward: true, poolOverride: null)
            .ToList();

        var relays = results.Where(r => r.IsRelay == true).ToList();
        Assert.Equal(80, relays.Count);

        // Полоса читается из названия события: у открытых сверху полос EventStyleAge
        // сознательно остаётся числом (RelayEventAge: верхняя граница > 18 — masters-логика).
        var bands = relays.GroupBy(r => r.Event).ToDictionary(g => g.Key, g => g.ToList());
        Assert.Equal(8, bands.Count);

        foreach (var style in new[] { "חופשי", "מעורב" })
        {
            Assert.Contains($"4X50 {style} שליחים - בנות 14-15", bands.Keys);
            Assert.Contains($"4X50 {style} שליחים - בנים 15-16", bands.Keys);
            Assert.Contains($"4X50 {style} שליחים - נשים 16-99", bands.Keys);
            Assert.Contains($"4X50 {style} שליחים - גברים 17-99", bands.Keys);
        }

        // Состав полос — ровно как события live-зачёта loglig 14669.
        Assert.Equal(11, bands["4X50 חופשי שליחים - בנות 14-15"].Count);
        Assert.Equal(14, bands["4X50 חופשי שליחים - בנים 15-16"].Count);
        Assert.Equal(8, bands["4X50 חופשי שליחים - נשים 16-99"].Count);
        Assert.Equal(7, bands["4X50 חופשי שליחים - גברים 17-99"].Count);
        Assert.Equal(10, bands["4X50 מעורב שליחים - בנות 14-15"].Count);
        Assert.Equal(14, bands["4X50 מעורב שליחים - בנים 15-16"].Count);
        Assert.Equal(8, bands["4X50 מעורב שליחים - נשים 16-99"].Count);
        Assert.Equal(8, bands["4X50 מעורב שליחים - גברים 17-99"].Count);

        // Победители полос — как в официальных событиях (в сквозном протоколе они шли
        // 1, 7, 17 и 26-м местами одной дисциплины).
        void AssertWinner(string band, string club, string time)
        {
            var w = Assert.Single(bands[band], r => r.Position == 1);
            Assert.Equal(club, w.Club);
            Assert.Equal(time, w.Time);
        }

        AssertWinner("4X50 מעורב שליחים - גברים 17-99", "מכבי מרום רמת גן", "01:41.58");
        AssertWinner("4X50 מעורב שליחים - נשים 16-99", "מכבי חיפה", "02:00.81");
        AssertWinner("4X50 מעורב שליחים - בנים 15-16", "מכבי מרום רמת גן", "01:49.09");
        AssertWinner("4X50 מעורב שליחים - בנות 14-15", "מכבי ראשון לציון SEALS", "02:04.84");
        AssertWinner("4X50 חופשי שליחים - גברים 17-99", "מכבי מרום רמת גן", "01:33.88");
        AssertWinner("4X50 חופשי שליחים - נשים 16-99", "מכבי חיפה", "01:47.69");
        AssertWinner("4X50 חופשי שליחים - בנים 15-16", "מכבי חיפה", "01:39.22");
        AssertWinner("4X50 חופשי שליחים - בנות 14-15", "מכבי קרית ביאליק", "01:53.34");

        // Внутри полосы места плотные с первого, NS — без места.
        foreach (var rows in bands.Values)
        {
            var placed = rows.Where(r => r.Position is not null).OrderBy(r => r.Position).ToList();
            Assert.Equal(Enumerable.Range(1, placed.Count), placed.Select(r => r.Position!.Value));
            Assert.All(rows.Where(r => r.TimeFail), r => Assert.Null(r.Position));
        }
        var ns = relays.Where(r => r.TimeFail).ToList();
        Assert.Equal(2, ns.Count);   // נצרת в вольной 15-16 и נהריה в вольной 16-99

        // Клубный зачёт эстафет по правилу 5 (25/22/20/…/1, ×2): 2638 из официальных 2672.
        // Остаток 34 невоспроизводим: движок loglig в полосе «בנים 15-16» вольной выдал ДВА
        // первых места по 50 (строка נהריה 01:49.45 напечатана в протоколе первой, хотя
        // медленнее семи других) — протокольный пересчёт по времени так не делает.
        int[] scale = [25, 22, 20, 18, 17, 16, 15, 14, 13, 12, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1];
        var clubPoints = relays
            .Where(r => r.Position is >= 1 and <= 20)
            .Sum(r => scale[r.Position!.Value - 1] * 2);
        Assert.Equal(2638, clubPoints);
    }

    /// <summary>
    /// Сетки полос — из регламента МАККАБИ, и применяются только к его протоколам. Эстафеты
    /// «без категории» бывают и у ARENA (бугрим 1562, צעירים 1512), но там организатор полос
    /// не делает — выдуманные полосы переписали бы им места. Ограничитель — маркер
    /// «אליפות מכבי» в названии протокола.
    /// </summary>
    [Fact]
    public void ForeignProtocol_WithSameHeaderShape_IsLeftAsPrinted()
    {
        using var fs = File.OpenRead(PdfPath);
        var comps = IsrOrgCompetitionParser.ParseCompetitions(fs, "he").ToList();
        Assert.All(comps, c => Assert.Contains("אליפות מכבי", c.Competition));

        // Тот же файл под чужим названием: заголовки эстафет те же, полос быть не должно.
        var renamed = comps
            .Select(c => c with { Competition = "אליפות ישראל \"ארנה\" בוגרים קיץ 2026" })
            .ToList();

        var relays = IsrOrgParser.MapHebrewOnly(
                renamed, country: "IL", isMastersFile: false, isAward: true, poolOverride: null)
            .Where(r => r.IsRelay == true)
            .ToList();

        Assert.Equal(80, relays.Count);
        Assert.All(relays, r => Assert.Equal("none", r.EventStyleGender));
        Assert.All(relays, r => Assert.DoesNotContain(" - ", r.Event));
        // Места остались сквозными по всей дисциплине: 1..40 в каждой из двух.
        foreach (var g in relays.GroupBy(r => r.EventStyleName))
            Assert.Equal(Enumerable.Range(1, 40), g.Select(r => r.Position!.Value).OrderBy(p => p));
    }

    /// <summary>
    /// Сетка выбирается на весь файл: у «צעירים» все возрасты ≤14 → младшая сетка, и файл
    /// «נוער ובוגרים» её не должен перетягивать. Проверяется тем, что старший файл не
    /// содержит ни одной детской полосы, а младший (соседний тест) — ни одной взрослой.
    /// </summary>
    [Fact]
    public void SeniorFile_DoesNotUseYouthGrid()
    {
        using var fs = File.OpenRead(PdfPath);
        var relays = IsrOrgParser.MapHebrewOnly(
                IsrOrgCompetitionParser.ParseCompetitions(fs, "he"),
                country: "IL", isMastersFile: false, isAward: true, poolOverride: null)
            .Where(r => r.IsRelay == true)
            .ToList();

        foreach (var youthBand in new[] { "9-11", "12-13", "9-10", "11-12", "13-14" })
            Assert.DoesNotContain(relays, r => r.Event.EndsWith(youthBand, StringComparison.Ordinal));
    }
}
