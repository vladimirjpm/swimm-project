using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регрессия по реальному протоколу «אליפות מכבי בשחייה אביב 2026 - צעירים»
/// (OrgCompId 16817, loglig 14668, 16.05.2026): у Маккаби заголовок эстафеты идёт
/// ВООБЩЕ без категории («4X50 חופשי שליחים»), а полосу пола/возраста протокол не
/// печатает — места сквозные по всей дисциплине, команды идут сразу после заголовка.
///
/// До фикса заголовок выставлял только pendingRelay* (masters-механика ARENA, где
/// категорию довешивает строка «מאסטרס …») и ждал строку категории, которой нет, —
/// обе эстафетные дисциплины (152 команды) молча выпадали из импорта, клубный зачёт
/// терял очки эстафет (×2) и не сходился с официальной таблицей. Теперь событие
/// материализует первая же командная строка: gender="none", возраст пустой.
/// </summary>
public class IsrOrgCompetitionParserMaccabiRelayTests
{
    private static string PdfPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "isr-maccabi-spring-2026-youth-he.pdf");

    [Fact]
    public void RelayHeaderWithoutCategory_MaterializesRelayEvents()
    {
        Assert.True(File.Exists(PdfPath), $"Фикстура протокола не найдена: {PdfPath}");

        using var fs = File.OpenRead(PdfPath);
        var comps = IsrOrgCompetitionParser.ParseCompetitions(fs, "he").ToList();

        // 59 индивидуальных событий + 2 эстафетные дисциплины.
        Assert.Equal(61, comps.Count);

        var relays = comps.Where(c => c.Results.Any(r => r.IsRelay == true)).ToList();
        Assert.Equal(2, relays.Count);

        var free = Assert.Single(relays, c => c.EventStyleName == "freestyle");
        // Канон комплексных эстафет в этом импорте — individual_medley (NormalizeStyleName
        // схлопывает medley; все существующие relay-строки БД лежат так же).
        var medley = Assert.Single(relays, c => c.EventStyleName == "individual_medley");
        Assert.All(relays, c =>
        {
            Assert.Equal("4X50", c.EventStyleLen);
            // Полосы в протоколе не печатаются — пол и возраст события пустые.
            Assert.Equal("none", c.EventStyleGender);
            Assert.Equal(string.Empty, c.EventStyleAge);
        });

        // Все команды на месте, места сквозные и уникальные, состав — ровно 4 ноги.
        Assert.Equal(79, free.Results.Count);
        Assert.Equal(73, medley.Results.Count);
        foreach (var c in relays)
        {
            var positions = c.Results.Select(r => (int)r.Position!).ToList();
            Assert.Equal(Enumerable.Range(1, c.Results.Count), positions.OrderBy(p => p));
            Assert.All(c.Results, r => Assert.Equal(4, r.RelaySwimmers!.Count));
            // Мусор из заголовков соседних страниц не должен попадать в ноги.
            Assert.All(c.Results.SelectMany(r => r.RelaySwimmers!), s =>
                Assert.InRange(s.BirthYear ?? 0, 2010, 2019));
        }

        // Статусные команды: DQ/NS в вольном, DNF/NS в комплексном — без времени, с пометкой.
        var freeFails = free.Results.Where(r => r.Time == null).Select(r => r.TimeFailNote).ToList();
        Assert.Equal(["DQ", "NS"], freeFails);
        var medleyFails = medley.Results.Where(r => r.Time == null).Select(r => r.TimeFailNote).ToList();
        Assert.Equal(["DNF", "NS", "NS", "NS", "NS"], medleyFails);

        // Победитель вольной эстафеты — по протоколу.
        var winner = free.Results.Single(r => (int)r.Position! == 1);
        Assert.Equal("מכבי מרום רמת גן", winner.Club);
        Assert.Equal("01:48.36", winner.Time);

        // Индивидуальные события фиксом не задеты: 2189 строк, эстафетных среди них нет.
        var individual = comps.Except(relays).SelectMany(c => c.Results).ToList();
        Assert.Equal(2189, individual.Count);
        Assert.All(individual, r => Assert.NotEqual(true, r.IsRelay));
    }

    /// <summary>
    /// Полный HE-путь импорта (MapHebrewOnly = ParseCompetitions + RelayBandReconstructor):
    /// сквозные эстафеты Маккаби разбиваются на зачётные полосы «возрастная группа × пол»
    /// с пересчётом мест внутри полосы — так организатор считает клубный зачёт.
    /// Пол ног восстановлен из индивидуальных заплывов того же файла (функция файла:
    /// Gender входит в ключ upsert, недетерминизм = дубли на переимпорте, инцидент И-4).
    /// </summary>
    [Fact]
    public void MaccabiRelays_AreSplitIntoBands_WithReRankedPositions()
    {
        using var fs = File.OpenRead(PdfPath);
        var results = IsrOrgParser.MapHebrewOnly(
                IsrOrgCompetitionParser.ParseCompetitions(fs, "he"),
                country: "IL", isMastersFile: false, isAward: true, poolOverride: null)
            .ToList();

        var relayRows = results.Where(r => r.IsRelay == true).ToList();
        Assert.Equal(152, relayRows.Count);
        Assert.Equal(2189 + 152, results.Count);

        // Полосы одной дисциплины — НЕ prelim/final пары: эвристика AssignHeatTypes сравнивает
        // людей состава, а не «клуб+имя команды» (по клубам полосы склеивались в ложные пары,
        // и prelim-места теряли очки зачёта). Всё соревнование — timed final.
        Assert.All(results, r => Assert.Null(r.HeatType));

        // Полос — по пять на дисциплину, сетка из регламента Маккаби (loglig doc 3185):
        // девочки 9-11 и 12-13 (шире стандартных групп), мальчики 9-10, 11-12, 13-14.
        var bands = relayRows
            .GroupBy(r => (r.EventStyleName, r.EventStyleGender, r.EventStyleAge))
            .ToDictionary(g => g.Key, g => g.ToList());
        Assert.Equal(10, bands.Count);
        foreach (var k in bands.Keys)
            Assert.Contains(k.EventStyleAge, k.EventStyleGender == "female"
                ? new[] { "9-11", "12-13" }
                : new[] { "9-10", "11-12", "13-14" });

        // Внутри каждой полосы места плотные с первого, DQ/NS/DNF — без места.
        foreach (var rows in bands.Values)
        {
            var placed = rows.Where(r => r.Position is not null).ToList();
            Assert.True(placed.Count > 0);
            Assert.Contains(placed, r => r.Position == 1);
            Assert.All(rows.Where(r => r.TimeFail), r => Assert.Null(r.Position));
        }

        // Вольная, мальчики 13-14: 16 команд, победитель — сквозной победитель дисциплины.
        var free1314M = bands[("freestyle", "male", "13-14")];
        Assert.Equal(16, free1314M.Count);
        var free1314Winner = Assert.Single(free1314M, r => r.Position == 1);
        Assert.Equal("מכבי מרום רמת גן", free1314Winner.Club);
        Assert.Equal("01:48.36", free1314Winner.Time);
        // Сквозное 14-е место (после вклинившейся команды 11-12) в полосе становится 13-м.
        Assert.Equal(13, Assert.Single(free1314M, r => r.Club == "מכבי כוכב יאיר - צור יגאל").Position);

        // Вольная, мальчики 11-12: сквозное 13-е место — первое своей полосы.
        var free1112M = bands[("freestyle", "male", "11-12")];
        var free1112Winner = Assert.Single(free1112M, r => r.Position == 1);
        Assert.Equal("מכבי וייסגל רחובות", free1112Winner.Club);
        Assert.Equal("02:03.13", free1112Winner.Time);

        // Вольная, девочки 12-13 (полоса регламента): места — абсолютные внутри полосы,
        // ровно как в live-зачёте loglig (событие «בנות 12-13»): победитель хайфа 02:03.57.
        var free1213F = bands[("freestyle", "female", "12-13")];
        var free1213Winner = Assert.Single(free1213F, r => r.Position == 1);
        Assert.Equal("מכבי חיפה", free1213Winner.Club);
        Assert.Equal("02:03.57", free1213Winner.Time);
        // Равные времена (02:05.17) делят место, следующий его пропускает.
        Assert.Equal(2, free1213F.Count(r => r.Position == 5));
        Assert.DoesNotContain(free1213F, r => r.Position == 6);

        // Вольная, девочки 9-11: победитель SEALS 02:12.91 (в сквозном протоколе — 38-я),
        // как в официальном событии «בנות 9-11».
        var free911F = bands[("freestyle", "female", "9-11")];
        var free911Winner = Assert.Single(free911F, r => r.Position == 1);
        Assert.Equal("מכבי ראשון לציון SEALS", free911Winner.Club);
        Assert.Equal("02:12.91", free911Winner.Time);
        Assert.Equal(15, free911F.Count);

        // Комплексная: четыре NS и один DNF — все без места, время пустое.
        var medleyFails = relayRows
            .Where(r => r.EventStyleName == "individual_medley" && r.TimeFail)
            .ToList();
        Assert.Equal(5, medleyFails.Count);
        Assert.All(medleyFails, r => Assert.Null(r.Position));
    }
}
