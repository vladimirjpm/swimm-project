using System.Text.Json;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers.Loglig;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сборка строк импорта из пособытийного источника loglig (шаг 3, docs/data-integrity.md §10).
/// Главное, что здесь решается: раунд источника (<c>Round</c>) — факт, а <c>HeatType</c> —
/// наш вывод об ОТБОРЕ, на котором висит Р34 «место в предварительном — не награда».
/// </summary>
public class LogligImportBuilderTests
{
    private static LogligImportContext Context() =>
        new("IL", "אליפות ישראל arena לנוער", "21/07/2026", "50m", IsAward: true);

    private static LogligResultRowDto Row(
        string round, string category, int position, string name = "מיכל אוגינץ", int year = 2012) =>
        new(position, round, category, name, year, "הפועל בית שמש", 4, 4, "00:26.62", null, 697, 5, 25);

    private static LogligEventResultsDto Event(string ageBand, params LogligResultRowDto[] rows) =>
        new("אליפות", "19/07/2026", "freestyle", "50", "female", ageBand, IsRelay: false, rows);

    private static List<Dictionary<string, JsonElement>> Parse(string json) =>
        JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json)!;

    private static string Build(params LogligEventResultsDto[] events) =>
        new LogligImportBuilder().BuildResultsJson(events, Context(),
            r => (r.FullName.Split(' ')[^1], string.Join(' ', r.FullName.Split(' ')[..^1])));

    /// <summary>
    /// Мокдамот при живом финале — отбор: HeatType=prelim, и Р34 гасит место как награду.
    /// Утренний прямой финал и вечерний финал — оба награда, HeatType=final у обоих, а
    /// различает их Round.
    /// </summary>
    [Fact]
    public void PrelimWithFinal_IsMarkedAsEliminator()
    {
        var json = Build(
            Event("14", Row(LogligRounds.Prelim, "בנות 14", 3)),
            Event("13-99", Row(LogligRounds.Final, "בנות 14", 1)));

        var rows = Parse(json);
        Assert.Equal(2, rows.Count);

        var prelim = rows.Single(r => r["round"].GetString() == LogligRounds.Prelim);
        Assert.Equal("prelim", prelim["heat_type"].GetString());

        var final = rows.Single(r => r["round"].GetString() == LogligRounds.Final);
        Assert.Equal("final", final["heat_type"].GetString());
    }

    /// <summary>
    /// Финал отменён (регламент: в нём осталось ≤ 2 участника) — медали и очки даёт утро.
    /// Пометить его prelim значило бы стереть официальные награды.
    /// </summary>
    [Fact]
    public void PrelimWithoutFinal_CountsAsResult()
    {
        var json = Build(Event("14", Row(LogligRounds.Prelim, "בנות 14", 1)));

        var row = Assert.Single(Parse(json));
        Assert.Equal(LogligRounds.Prelim, row["round"].GetString());
        Assert.Equal(JsonValueKind.Null, row["heat_type"].ValueKind);
    }

    /// <summary>«Есть финал» считается по ДИСЦИПЛИНЕ с категорией, а не по всему файлу.</summary>
    [Fact]
    public void FinalOfAnotherCategory_DoesNotSilenceThisPrelim()
    {
        var json = Build(
            Event("14", Row(LogligRounds.Prelim, "בנות 14", 1)),
            Event("15", Row(LogligRounds.Final, "בנות 15", 1, "נטלי הלמן", 2011)));

        var prelim = Parse(json).Single(r => r["round"].GetString() == LogligRounds.Prelim);
        Assert.Equal(JsonValueKind.Null, prelim["heat_type"].ValueKind);
    }

    /// <summary>
    /// Пол и возраст берутся из СЕКЦИИ строки: у вечернего финала шапка события открытая
    /// («נשים 13-99»), а зачёт идёт по возрастным секциям внутри него.
    /// </summary>
    [Fact]
    public void SectionCategory_WinsOverOpenEventHeader()
    {
        var json = Build(Event("13-99", Row(LogligRounds.Final, "בנות 14", 1)));

        var row = Assert.Single(Parse(json));
        Assert.Equal("female", row["event_style_gender"].GetString());
        Assert.Equal("14", row["event_style_age"].GetString());
    }

    /// <summary>Эстафетные события источник несёт без состава — в импорт они не идут.</summary>
    [Fact]
    public void RelayEvents_AreSkipped()
    {
        var relay = new LogligEventResultsDto(
            "אליפות", "19/07/2026", "freestyle", "4X50", "female", "14-15", IsRelay: true,
            [Row(LogligRounds.Final, "בנות 14-15", 1, "מכבי חיפה")]);

        Assert.Empty(Parse(Build(relay)));
    }
}
