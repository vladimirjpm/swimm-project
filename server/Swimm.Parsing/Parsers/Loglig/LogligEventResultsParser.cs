using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Swimm.Parsing.Helpers;

namespace Swimm.Parsing.Parsers.Loglig;

/// <summary>Раунд зачёта в терминах <c>Results.Round</c>.</summary>
public static class LogligRounds
{
    /// <summary>«גמר ישיר» — прямой финал. У чемпионатов «мокдамот и финал» это утренний
    /// зачёт возрастных групп: медали вручают там же, очки платят полностью.</summary>
    public const string TimedFinal = "timed-final";

    /// <summary>«גמר» — вечерний финал первенства.</summary>
    public const string Final = "final";

    /// <summary>«מוקדמות» — предварительные, отбор в финал.</summary>
    public const string Prelim = "prelim";
}

/// <summary>Строка результата одного события loglig.</summary>
public sealed record LogligResultRow(
    int? Position,
    string Round,
    string Category,
    string FullName,
    int? BirthYear,
    string Club,
    int Heat,
    int Lane,
    string? Time,
    string? FailNote,
    int InternationalPoints,
    int? PersonalPoints,
    int? ClubPoints);

/// <summary>Одно событие loglig целиком: шапка дисциплины + строки всех его секций.</summary>
public sealed record LogligEventResults(
    string CompetitionName,
    string Date,
    string StyleName,
    string Distance,
    string Gender,
    string AgeBand,
    bool IsRelay,
    IReadOnlyList<LogligResultRow> Rows);

/// <summary>
/// Разбор страницы результатов ОДНОГО события loglig
/// (<c>LeagueTable/AthleticsDisciplineResults/{eventId}?isModal=True&amp;showCategories=True</c>).
///
/// Зачем при живом PDF-парсере. PDF-экспорт того же соревнования печатает утреннюю и вечернюю
/// сессии ОДНИМ списком, пересортированным по времени: финалист занимает два места подряд,
/// а раунда в файле нет вообще (И13, docs/data-integrity.md §10). Сайт держит их разными
/// событиями и подписывает секции — «גמר ישיר», «גמר», «מוקדמות», — поэтому только отсюда
/// можно узнать <c>Results.Round</c> и посчитать зачёт так же, как организатор.
///
/// Чистая функция: HTML на входе, модели на выходе, ни сети, ни БД — тестируется на фикстурах.
/// </summary>
public static partial class LogligEventResultsParser
{
    /// <summary>Разбирает страницу события: шапка дисциплины + строки всех секций.</summary>
    public static LogligEventResults Parse(string html)
    {
        var headers = H4Rx().Matches(html).Select(m => Clean(m.Groups[1].Value)).ToList();
        var (competition, date) = ParseCompetitionHeader(headers);
        var (style, distance, gender, ageBand, isRelay) = ParseDisciplineHeader(headers);

        var rows = new List<LogligResultRow>();
        var round = string.Empty;
        var category = string.Empty;

        foreach (Match tr in RowRx().Matches(html))
        {
            var cells = CellRx().Matches(tr.Groups[1].Value).Select(m => Clean(m.Groups[1].Value)).ToList();

            // Подзаголовок секции — одна ячейка на всю ширину: «גמר ישיר - בנות 14».
            if (cells.Count == 1 && LooksLikeSection(cells[0]))
            {
                (round, category) = ParseSection(cells[0]);
                continue;
            }

            // Данные: 10 колонок у личных (есть год рождения), 9 у эстафет.
            if (cells.Count is not (9 or 10)) continue;
            if (cells[0] == "מיקום") continue;   // строка заголовков таблицы

            var row = ParseRow(cells, round, category, hasBirthYear: cells.Count == 10);
            if (row is not null) rows.Add(row);
        }

        return new LogligEventResults(competition, date, style, distance, gender, ageBand, isRelay, rows);
    }

    private static LogligResultRow? ParseRow(List<string> cells, string round, string category, bool hasBirthYear)
    {
        var i = 0;
        var position = ParseInt(cells[i++]);
        var name = cells[i++];
        if (string.IsNullOrWhiteSpace(name)) return null;

        var birthYear = hasBirthYear ? ParseInt(cells[i++]) : null;
        var club = cells[i++];
        var heat = ParseInt(cells[i++]) ?? 0;
        var lane = ParseInt(cells[i++]) ?? 0;
        var (time, failNote) = ParseTime(cells[i++]);
        var fina = ParseInt(cells[i++]) ?? 0;
        var personal = ParseInt(cells[i++]);
        var clubPoints = ParseInt(cells[i]);

        return new LogligResultRow(
            position, round, category, name, birthYear, club, heat, lane,
            time, failNote, fina, personal, clubPoints);
    }

    /// <summary>«אליפות … קיץ 2026 - 19/07/2026» → имя и дата (дата — в конце строки).</summary>
    private static (string Competition, string Date) ParseCompetitionHeader(List<string> headers)
    {
        foreach (var h in headers)
        {
            var m = DateRx().Match(h);
            if (!m.Success) continue;
            var name = h[..m.Index].TrimEnd(' ', '-');
            return (name.Trim(), m.Value);
        }
        return (string.Empty, string.Empty);
    }

    /// <summary>
    /// «50 חופשי - בנות 14 - תוצאות» → дистанция, стиль, пол, возрастная полоса.
    /// У эстафет дистанция печатается как «4X50», а в названии стоит «שליחים».
    /// </summary>
    private static (string Style, string Distance, string Gender, string AgeBand, bool IsRelay)
        ParseDisciplineHeader(List<string> headers)
    {
        var header = headers.FirstOrDefault(h => h.EndsWith("תוצאות", StringComparison.Ordinal)
                                                 && h.Contains('-'))
                     ?? string.Empty;

        var parts = SplitOnDash(header).ToList();
        // Хвост «תוצאות» — не часть дисциплины.
        if (parts.Count > 0 && parts[^1] == "תוצאות") parts.RemoveAt(parts.Count - 1);

        var discipline = parts.Count > 0 ? parts[0] : string.Empty;
        var category = parts.Count > 1 ? string.Join(" - ", parts.Skip(1)) : string.Empty;

        var distanceMatch = DistanceRx().Match(discipline);
        var distance = distanceMatch.Success ? distanceMatch.Value : string.Empty;
        var styleText = distanceMatch.Success
            ? discipline[(distanceMatch.Index + distanceMatch.Length)..].Trim()
            : discipline;
        var isRelay = styleText.Contains("שליחים", StringComparison.Ordinal);

        var (gender, ageBand) = ParseCategory(category);
        return (StyleOf(styleText), distance, gender, ageBand, isRelay);
    }

    /// <summary>«בנות 14» → female + «14»; у общего финала категория «כללי» — пола нет.</summary>
    private static (string Gender, string AgeBand) ParseCategory(string category)
    {
        var tokens = category.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var gender = HebrewTextHelper.NormalizeGenderHE(tokens.FirstOrDefault() ?? string.Empty);
        var age = string.Join(' ', tokens.Skip(1)).Trim();
        return (gender, age);
    }

    /// <summary>Стиль из ивритского названия; «מעורב שליחים» — комбинированная эстафета.</summary>
    private static string StyleOf(string styleText)
    {
        var cleaned = styleText.Replace("שליחים", string.Empty, StringComparison.Ordinal).Trim();
        if (cleaned.Length == 0) return "freestyle";
        foreach (var (he, en) in HebrewTextHelper.StyleMapHE)
            if (cleaned.Contains(he, StringComparison.Ordinal))
                return HebrewTextHelper.NormalizeStyleName(en);
        return cleaned;
    }

    /// <summary>Подзаголовок секции — «гмар/мокдамот», с категорией или без.</summary>
    private static bool LooksLikeSection(string text) =>
        text.Contains("גמר", StringComparison.Ordinal) || text.Contains("מוקדמות", StringComparison.Ordinal);

    /// <summary>«גמר ישיר - בנות 14» → раунд + категория секции.</summary>
    private static (string Round, string Category) ParseSection(string caption)
    {
        var parts = SplitOnDash(caption);
        var head = parts.Length > 0 ? parts[0] : caption;
        var category = parts.Length > 1 ? string.Join(" - ", parts.Skip(1)) : string.Empty;

        var round = head.Contains("מוקדמות", StringComparison.Ordinal) ? LogligRounds.Prelim
            : head.Contains("ישיר", StringComparison.Ordinal) ? LogligRounds.TimedFinal
            : LogligRounds.Final;
        return (round, category);
    }

    /// <summary>
    /// Время или статус. Кроме «00:26.62» в ячейке бывает «00:33.44 NMin» (норматив не выполнен,
    /// время настоящее) и чистые статусы DQ/NS/DNF, у которых времени нет вовсе.
    /// </summary>
    private static (string? Time, string? FailNote) ParseTime(string cell)
    {
        var m = TimeRx().Match(cell);
        if (!m.Success) return (null, string.IsNullOrWhiteSpace(cell) ? null : cell);

        var note = cell.Remove(m.Index, m.Length).Trim();
        return (m.Value, string.IsNullOrWhiteSpace(note) ? null : note);
    }

    /// <summary>
    /// Разделитель полей в заголовках — тире С ПРОБЕЛАМИ. Резать по голому «-» нельзя:
    /// возрастная полоса пишется через тире («13-99», «17-18»), и «50 חופשי - נשים 13-99»
    /// распадалось бы на «13» и «99».
    /// </summary>
    private static string[] SplitOnDash(string text) =>
        DashRx().Split(text)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

    private static int? ParseInt(string s) => int.TryParse(s.Replace(",", string.Empty), out var v) ? v : null;

    /// <summary>Текст ячейки: без тегов, без html-сущностей, схлопнутые пробелы.</summary>
    private static string Clean(string html) =>
        SpaceRx().Replace(WebUtility.HtmlDecode(TagRx().Replace(html, " ")), " ").Trim();

    [GeneratedRegex("""<h4[^>]*>(.*?)</h4>""", RegexOptions.Singleline)]
    private static partial Regex H4Rx();

    [GeneratedRegex("""<tr[^>]*>(.*?)</tr>""", RegexOptions.Singleline)]
    private static partial Regex RowRx();

    [GeneratedRegex("""<t[dh][^>]*>(.*?)</t[dh]>""", RegexOptions.Singleline)]
    private static partial Regex CellRx();

    [GeneratedRegex("""<[^>]+>""", RegexOptions.Singleline)]
    private static partial Regex TagRx();

    [GeneratedRegex("""\s+""")]
    private static partial Regex SpaceRx();

    [GeneratedRegex("""\s+-\s+""")]
    private static partial Regex DashRx();

    [GeneratedRegex("""\d{2}/\d{2}/\d{4}""")]
    private static partial Regex DateRx();

    [GeneratedRegex("""^\d+[xX]\d+|^\d+""")]
    private static partial Regex DistanceRx();

    [GeneratedRegex("""\d{1,2}:\d{2}\.\d{2}""")]
    private static partial Regex TimeRx();
}
