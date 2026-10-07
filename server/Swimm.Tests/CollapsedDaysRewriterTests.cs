using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Swimm.Parsing.Discovery;
using Swimm.Parsing.Parsers.Loglig;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Импорт многодневки: даты дисциплин из сетки loglig вместо одной даты протокола
/// (docs/plans/multiday-split-plan.md, М3). Без этого перезалив разделённого соревнования
/// (#1645 → дни 03/01 и 04/01) сложил бы все строки во второй день — дубли заплывов первого.
/// </summary>
public class CollapsedDaysRewriterTests
{
    private static DisciplineDates Grid11586()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "loglig",
            "disciplines-11586-masters-winter-2025.html"));
        return LogligDisciplineDateSource.FromGrid(LogligStartListParser.ParseDisciplineGrid(html)
            .Select(r => new LogligDisciplineGridRowDto(
                r.DisciplineId, r.EventNumber, r.DisciplineRaw, r.Category, r.StyleName, r.Distance,
                r.Gender, r.AgeBand, r.IsRelay, r.MinTime, r.StartAtLocal, r.Registered, r.Participants)));
    }

    private const string Name = "אליפות ישראל arena למאסטרס חורף 2025";

    private static JsonObject Row(string style, string len, string gender, string last, int lane, string date = "04/01/2025") => new()
    {
        ["country"] = "ISR", ["competition"] = Name, ["date"] = date,
        ["event_style_name"] = style, ["event_style_len"] = len, ["event_style_gender"] = gender,
        ["pool_type"] = "25m", ["position"] = lane, ["heat"] = 1, ["lane"] = lane,
        ["last_name"] = last, ["first_name"] = "יעל", ["birth_year"] = 1985, ["club"] = "מכבי חיפה",
        ["time"] = "05:00.00",
    };

    private static ParsedCompetition Parsed(params JsonObject[] rows)
    {
        var json = new JsonArray(rows.Select(r => r.DeepClone()).ToArray()).ToJsonString();
        return new ParsedCompetition
        {
            Format = "IsrOrg", ResultsJson = json, ResultCount = rows.Length,
            Competitions = rows.GroupBy(r => (string?)r["date"]).Select(g => new ParsedCompetitionSummary(Name, g.Key!, g.Count(), "25m")).ToList(),
        };
    }

    [Fact]
    public void OneDateProtocol_GetsDisciplineDates_AndTwoDaySummary()
    {
        var parsed = Parsed(
            Row("freestyle", "400", "female", "א", 1),          // 03/01
            Row("butterfly", "200", "female", "ב", 2),          // 03/01 — вечер первого дня
            Row("butterfly", "200", "male", "ג", 3),            // 04/01 — утро второго
            Row("individual_medley", "100", "none", "ד", 4));   // без пола: оба пола 03/01

        var outcome = CollapsedDaysRewriter.Rewrite(parsed, Grid11586());

        Assert.True(outcome.Rewritten);
        Assert.True(outcome.Warn);
        var dates = JsonNode.Parse(outcome.Parsed.ResultsJson)!.AsArray().Select(r => (string?)r!["date"]).ToList();
        Assert.Equal(["03/01/2025", "03/01/2025", "04/01/2025", "03/01/2025"], dates);
        Assert.Equal([("03/01/2025", 3), ("04/01/2025", 1)],
            outcome.Parsed.Competitions.Select(c => (c.Date, c.ResultCount)));
        Assert.Contains("אליפות", outcome.Parsed.ResultsJson); // иврит не экранирован
    }

    [Fact]
    public void ProtocolThatAlreadyHasDates_OrOneDaySource_IsNotTouched()
    {
        var twoDates = Parsed(Row("freestyle", "400", "female", "א", 1, "03/01/2025"), Row("butterfly", "200", "male", "ב", 2));
        var kept = CollapsedDaysRewriter.Rewrite(twoDates, Grid11586());
        Assert.False(kept.Rewritten);
        Assert.False(kept.Warn);
        Assert.Same(twoDates, kept.Parsed);

        var oneDay = new DisciplineDates([(new DisciplineDayKey("freestyle", "400", "female"), new DateOnly(2025, 1, 4))]);
        var single = Parsed(Row("freestyle", "400", "female", "א", 1));
        Assert.False(CollapsedDaysRewriter.Rewrite(single, oneDay).Warn); // обычный однодневный — молчим
    }

    [Fact]
    public void RowWithoutDay_KeepsWholeProtocol_AndSaysWhy()
    {
        var parsed = Parsed(Row("freestyle", "400", "female", "א", 1), Row("freestyle", "1500", "male", "ב", 2));

        var outcome = CollapsedDaysRewriter.Rewrite(parsed, Grid11586());

        Assert.False(outcome.Rewritten);
        Assert.True(outcome.Warn);
        Assert.Contains("freestyle 1500 male", outcome.Message);
        Assert.Same(parsed, outcome.Parsed);
    }

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static Stream ToStream(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    /// <summary>
    /// Приёмка М3: перезалив поверх разделённого соревнования (событие, день 1 без compID, день 2
    /// с ним — как #1675/#1645 после --split-days) раскладывает строки по СВОИМ дням и ничего не
    /// задваивает; ручная пометка на строке первого дня переживает перезалив.
    /// </summary>
    [Fact]
    public async Task ReimportAfterSplit_KeepsRowsInTheirDays_NoDuplicates()
    {
        await using var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(nameof(ReimportAfterSplit_KeepsRowsInTheirDays_NoDuplicates))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        var ev = new CompetitionEvent { Name = Name, OrgCompId = 6595 };
        db.CompetitionEvents.Add(ev);
        await db.SaveChangesAsync();
        var day1 = new Competition { Name = Name, SubName = Name, Date = "03/01/2025", PoolType = "25m", EventId = ev.Id, DayNumber = 1 };
        var day2 = new Competition { Name = Name, SubName = Name, Date = "04/01/2025", PoolType = "25m", EventId = ev.Id, DayNumber = 2, OrgCompId = 6595 };
        db.Competitions.AddRange(day1, day2);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row("freestyle", "400", "female", "א", 1), Row("butterfly", "200", "female", "ב", 2),
            Row("butterfly", "200", "male", "ג", 3),
        };
        var svc = new JsonImportService(db, new NullCache());
        var options = new ImportEventOptions(ev.Id, null, OverwriteExisting: true, DeleteMissing: true);

        // Первый импорт — уже с датами дисциплин (так строки лежат после --split-days).
        var first = CollapsedDaysRewriter.Rewrite(Parsed(rows), Grid11586()).Parsed;
        Assert.Empty((await svc.ImportAsync(ToStream(first.ResultsJson), "6595.pdf", null, options, 6595)).ErrorMessages);
        var flagged = await db.Results.SingleAsync(r => r.CompetitionId == day1.Id && r.Distance == "400");
        flagged.SuspectReason = "manual";
        flagged.SuspectIsManual = true;
        await db.SaveChangesAsync();

        // Перезалив: протокол снова «одной датой 04/01», шаг М3 возвращает даты дисциплин.
        var again = CollapsedDaysRewriter.Rewrite(Parsed(rows), Grid11586()).Parsed;
        Assert.Empty((await svc.ImportAsync(ToStream(again.ResultsJson), "6595.pdf", null, options, 6595)).ErrorMessages);

        Assert.Equal(2, await db.Competitions.CountAsync());
        Assert.Equal(2, await db.Results.CountAsync(r => r.CompetitionId == day1.Id));
        Assert.Equal(1, await db.Results.CountAsync(r => r.CompetitionId == day2.Id));
        Assert.True((await db.Results.SingleAsync(r => r.Id == flagged.Id)).SuspectIsManual);
    }
}
