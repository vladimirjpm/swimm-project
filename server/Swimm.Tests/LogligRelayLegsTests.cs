using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers.Loglig;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р107: состав эстафетных команд со страницы события loglig и сборка эстафетных строк импорта.
///
/// Фикстура — живая страница «4X50 חופשי שליחים» мальчиков лиги וייסגל 30.10.2026 (16890, наш
/// #1776, событие 85569). PDF того же соревнования склеил команды одного клуба из одного заплыва
/// и дорожки в блок по 8–12 фамилий, и разбор брал первые четыре — у 11 из 31 эстафеты на этапах
/// 2 и 4 стояли пловцы соседней команды. Страница события печатает состав каждой команды
/// ссылками на карточки — отсюда он и берётся.
/// </summary>
public class LogligRelayLegsTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "loglig", name));

    [Fact]
    public void RelayPage_LegsParsedInStageOrder_WithLogligIds()
    {
        var ev = LogligEventResultsParser.Parse(Fixture("event-85569-relay-boys-weisgal-legs.html"));

        Assert.True(ev.IsRelay);
        Assert.Equal(28, ev.Rows.Count);
        Assert.All(ev.Rows, r => Assert.NotNull(r.Legs));

        var weisgal2016 = ev.Rows.Single(r => r.Legs!.Any(l => l.LogligId == 303024));
        Assert.Equal(
            new[] { "אדם קלר", "ירון בבאי", "בניה שפיר צרלסוורט", "מיכאל קוזניצוב" },
            weisgal2016.Legs!.Select(l => l.FullName));
        Assert.Equal(new int?[] { 303024, 298486, 298519, 299584 }, weisgal2016.Legs!.Select(l => l.LogligId));

        // Команда, которую PDF перемешал: у нас на этапах 2 и 4 стояли אדם קלר и ירון בבאי.
        var weisgal2019 = ev.Rows.Single(r => r.Legs!.Any(l => l.FullName == "גיא להב"));
        Assert.Equal(
            new[] { "גיא להב", "ליעד אורן", "גיא אהרון", "יהב קורסונסקי" },
            weisgal2019.Legs!.Select(l => l.FullName));
        Assert.Equal(1, weisgal2019.Heat);
        Assert.Equal(3, weisgal2019.Lane);
    }

    /// <summary>Страницы 2025 года состав не печатали — без ссылок ног нет, без догадок по тексту.</summary>
    [Fact]
    public void OldRelayPage_WithoutPlayerLinks_HasNoLegs()
    {
        var ev = LogligEventResultsParser.Parse(Fixture("event-82438-relay-girls14-15.html"));

        Assert.True(ev.IsRelay);
        Assert.NotEmpty(ev.Rows);
        Assert.All(ev.Rows, r => Assert.Null(r.Legs));
    }

    [Fact]
    public void IndividualPage_HasNoLegs()
    {
        var ev = LogligEventResultsParser.Parse(Fixture("event-82330-morning-girls14.html"));

        Assert.False(ev.IsRelay);
        Assert.All(ev.Rows, r => Assert.Null(r.Legs));
    }

    private static LogligRelayLegDto Leg(int id, string name) => new(id, name);

    private static LogligResultRowDto Team(int lane, int position, params LogligRelayLegDto[] legs) =>
        new(position, LogligRounds.TimedFinal, "בנים 8-15", string.Join(", ", legs.Select(l => l.FullName)), null,
            "מכבי וייסגל רחובות", 1, lane, "03:01.23", null, 120, null, 18, legs);

    /// <summary>
    /// Строка эстафеты — как у PDF-разбора: имя и год первой ноги, состав по этапам, полоса из шапки
    /// события. Команда с несопоставленной ногой пропускается целиком; личные события — не сюда.
    /// </summary>
    [Fact]
    public void BuildRelayResultsJson_ResolvedTeamsOnly_LegsInOrder_BandFromHeader()
    {
        var full = Team(3, 1, Leg(1, "גיא להב"), Leg(2, "ליעד אורן"), Leg(3, "גיא אהרון"), Leg(4, "יהב קורסונסקי"));
        var broken = Team(4, 2, Leg(1, "גיא להב"), Leg(9, "לא ידוע"), Leg(3, "גיא אהרון"), Leg(4, "יהב קורסונסקי"));
        var relay = new LogligEventResultsDto("ליגה אזורית וייסגל", "30/10/2026", "freestyle", "4X50", "male", "8-15",
            IsRelay: true, [full, broken]);
        var individual = new LogligEventResultsDto("ליגה", "30/10/2026", "freestyle", "50", "male", "10",
            IsRelay: false, [new LogligResultRowDto(1, LogligRounds.TimedFinal, "בנים 10", "דן לוי", 2016,
                "מכבי וייסגל רחובות", 1, 4, "00:40.00", null, 100, null, 18)]);

        var known = new System.Collections.Generic.Dictionary<int, LogligResolvedLeg>
        {
            [1] = new("להב", "גיא", 2019),
            [2] = new("אורן", "ליעד", 2019),
            [3] = new("אהרון", "גיא", 2019),
            [4] = new("קורסונסקי", "יהב", 2019),
        };
        var json = new LogligImportBuilder().BuildRelayResultsJson(
            [relay, individual],
            new LogligImportContext("IL", "ליגה אזורית וייסגל 30/10", "30/10/2026", "25m", IsAward: true),
            leg => leg.LogligId is int id ? known.GetValueOrDefault(id) : null);

        var rows = JsonSerializer.Deserialize<JsonElement[]>(json)!;
        var row = Assert.Single(rows);
        Assert.True(row.GetProperty("is_relay").GetBoolean());
        Assert.Equal(3, row.GetProperty("lane").GetInt32());
        Assert.Equal("להב", row.GetProperty("last_name").GetString());
        Assert.Equal(2019, row.GetProperty("birth_year").GetInt32());
        Assert.Equal("8-15", row.GetProperty("event_style_age").GetString());
        Assert.Equal("03:01.23", row.GetProperty("time").GetString());
        Assert.Equal(18, row.GetProperty("official_club_points").GetInt32());
        Assert.Equal(
            new[] { "להב", "אורן", "אהרון", "קורסונסקי" },
            row.GetProperty("relay_swimmers").EnumerateArray().Select(s => s.GetProperty("last_name").GetString()));
        Assert.Equal(
            new[] { 1, 2, 3, 4 },
            row.GetProperty("relay_swimmers").EnumerateArray().Select(s => s.GetProperty("order").GetInt32()));
    }
}
