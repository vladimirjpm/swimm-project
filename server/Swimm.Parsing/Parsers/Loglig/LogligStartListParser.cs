using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Swimm.Parsing.Helpers;

namespace Swimm.Parsing.Parsers.Loglig;

/// <summary>
/// Одна строка СТАРТОВОГО протокола: кто, в каком заплыве, на какой дорожке и во сколько.
/// </summary>
/// <param name="LogligId">
/// id пловца на loglig — имя в стартовом протоколе напечатано ссылкой на карточку.
/// Главный (и практически единственный надёжный) ключ матчинга: имена тут только на иврите,
/// английского экспорта у этой вьюхи нет. null — ссылки не было (у источника бывает).
/// </param>
/// <param name="SeedTime">
/// Посевное время — «зман книса», личный рекорд пловца С ДРУГОГО старта, по которому его
/// посеяли. НЕ результат этого соревнования: показывать его как время заплыва нельзя.
/// null — в протоколе стоит «NT» (времени нет).
/// </param>
/// <param name="HeatStartAt">
/// Время старта ЗАПЛЫВА («שעת הזנקה») из подзаголовка секции — местное израильское,
/// без даты и без часового пояса. Дату даёт сетка дня (<see cref="LogligDisciplineGridRow"/>).
/// </param>
public sealed record LogligStartListRow(
    int Heat,
    int Lane,
    int? LogligId,
    string FullName,
    int? BirthYear,
    string Club,
    string? SeedTime,
    string Round,
    TimeOnly? HeatStartAt);

/// <summary>Стартовый протокол одного заплыва целиком: шапка + строки всех его секций.</summary>
/// <param name="DisciplineRaw">Дисциплина как напечатана: «100 מעורב אישי», «4X50 חופשי שליחים».</param>
public sealed record LogligStartList(
    string CompetitionName,
    string Date,
    string DisciplineRaw,
    string StyleName,
    string Distance,
    bool IsRelay,
    IReadOnlyList<LogligStartListRow> Rows);

/// <summary>
/// Строка сетки заплывов дня — программа соревнования.
/// </summary>
/// <param name="DisciplineId">
/// id ЗАПЛЫВА на loglig (76321…), не соревнования. Тот же id, что у соседних
/// <c>AthleticsDisciplineResults/{id}</c>, и он же — ключ идентичности заявки.
/// </param>
/// <param name="StartAtLocal">
/// Дата И время старта заплыва, МЕСТНЫЕ израильские (<c>Kind=Unspecified</c>): источник
/// печатает их без часового пояса. Перевод в UTC — забота вызывающего, не парсера.
/// Отсюда же берётся день многодневки: у источника он зашит в саму дату.
/// </param>
/// <param name="Registered">«סה"כ נרשמים» — сколько записалось (шире стартового протокола:
/// снявшихся до посева тут ещё видно).</param>
/// <param name="Participants">«סה"כ משתתפים» — сколько реально участвует.</param>
public sealed record LogligDisciplineGridRow(
    int DisciplineId,
    int? EventNumber,
    string DisciplineRaw,
    string Category,
    string StyleName,
    string Distance,
    string Gender,
    string AgeBand,
    bool IsRelay,
    string? MinTime,
    DateTime? StartAtLocal,
    int Registered,
    int Participants);

/// <summary>
/// Разбор двух страниц loglig, из которых состоит стартовый протокол
/// (docs/plans/start-list-plan.md §1):
///
/// <list type="bullet">
/// <item><c>LeagueTable/AthleticsDisciplines/{logligId}</c> — сетка заплывов дня:
///   программа, категории, ВРЕМЯ СТАРТА каждого заплыва, счётчики записавшихся;</item>
/// <item><c>LeagueTable/StartList/{disciplineId}?isModal=True</c> — сам стартовый протокол:
///   заплыв, дорожка, пловец с его loglig-id, посевное время.</item>
/// </list>
///
/// Зачем это отдельно от <see cref="LogligEventResultsParser"/>. Тот разбирает РЕЗУЛЬТАТЫ —
/// то, что уже проплыли. Здесь план: времени результата нет вовсе, зато есть время старта и
/// счётчик неявок (записалось 1056, участвует 989 — замер соревнования 14208). Смешивать их
/// в одну модель нельзя ровно по той же причине, по которой заявки не кладутся в
/// <c>Results</c>: это данные другого происхождения.
///
/// Чистая функция: HTML на входе, модели на выходе, ни сети, ни БД — тестируется на фикстурах.
/// </summary>
public static partial class LogligStartListParser
{
    // ── Стартовый протокол одного заплыва ────────────────────────────────────

    /// <summary>
    /// Разбирает <c>StartList/{disciplineId}?isModal=True</c>.
    ///
    /// Строки идут вперемешку с подзаголовками секций: «גמר ישיר» + «מקצה: 1 שעת הזנקה:10:00».
    /// Раунд и время старта запоминаются из подзаголовка и приписываются всем строкам ниже,
    /// пока не встретится следующий — так же, как <see cref="LogligEventResultsParser"/>
    /// разносит раунд по секциям.
    /// </summary>
    public static LogligStartList ParseStartList(string html)
    {
        var (competition, date, disciplineRaw) = ParseStartListTitle(html);
        var (style, distance, isRelay) = ParseDiscipline(disciplineRaw);

        // Год рождения — необязательная колонка: у части заплывов её в шапке нет.
        var hasBirthYear = html.Contains(BirthYearHeader, StringComparison.Ordinal);

        var rows = new List<LogligStartListRow>();
        var round = string.Empty;
        TimeOnly? heatStartAt = null;

        foreach (var rowHtml in TopLevelRows(html))
        {
            var cells = CellsOf(rowHtml);
            if (cells.Count == 0) continue;

            // Подзаголовок заплыва: одна ячейка с «מקצה: N שעת הזנקה:HH:MM», рядом — раунд.
            var heatCaption = cells.FirstOrDefault(c => HeatCaptionRx().IsMatch(c));
            if (heatCaption is not null)
            {
                heatStartAt = ParseHeatTime(heatCaption);
                var roundCell = cells.FirstOrDefault(LooksLikeRound);
                if (roundCell is not null) round = RoundOf(roundCell);
                continue;
            }

            if (cells[0] == HeatColumnHeader) continue;   // строка заголовков таблицы
            if (cells.Count < (hasBirthYear ? 6 : 5)) continue;

            var row = ParseStartListRow(cells, rowHtml, hasBirthYear, round, heatStartAt);
            if (row is not null) rows.Add(row);
        }

        return new LogligStartList(competition, date, disciplineRaw, style, distance, isRelay, rows);
    }

    private static LogligStartListRow? ParseStartListRow(
        List<string> cells, string rowHtml, bool hasBirthYear, string round, TimeOnly? heatStartAt)
    {
        var i = 0;
        var heat = ParseInt(cells[i++]);
        var lane = ParseInt(cells[i++]);
        if (heat is null || lane is null) return null;

        var name = cells[i++];
        if (string.IsNullOrWhiteSpace(name)) return null;

        var birthYear = hasBirthYear ? ParseInt(cells[i++]) : null;
        var club = cells[i++];
        var seedRaw = i < cells.Count ? cells[i] : string.Empty;

        // loglig-id берётся из СЫРОГО html строки: Clean() съедает ссылку вместе с тегами.
        var linkMatch = PlayerLinkRx().Match(rowHtml);
        int? logligId = linkMatch.Success && int.TryParse(linkMatch.Groups[1].Value, out var id) ? id : null;

        return new LogligStartListRow(
            heat.Value, lane.Value, logligId, name, birthYear, club,
            NormalizeSeedTime(seedRaw), round, heatStartAt);
    }

    /// <summary>
    /// «Start list - 100 מעורב אישי - אליפות … חורף 2026- מחוז דרום - 19/02/2026»
    /// → соревнование, дата, дисциплина.
    ///
    /// Режем по тире С ПРОБЕЛАМИ: в самом названии соревнования тире встречается и без них
    /// («2026- מחוז דרום»), и внутри возрастной полосы («לגילאי 8-11») — по голому «-»
    /// заголовок разваливался бы на куски.
    /// </summary>
    private static (string Competition, string Date, string Discipline) ParseStartListTitle(string html)
    {
        var titleMatch = TitleRx().Match(html);
        if (!titleMatch.Success) return (string.Empty, string.Empty, string.Empty);

        var title = Clean(titleMatch.Groups[1].Value);
        var parts = SplitOnDash(title).ToList();

        var date = string.Empty;
        var dateIndex = parts.FindIndex(p => DateRx().IsMatch(p));
        if (dateIndex >= 0)
        {
            date = DateRx().Match(parts[dateIndex]).Value;
            parts.RemoveAt(dateIndex);
        }

        // parts[0] — маркер вьюхи («Start list», на этой странице он английский даже в
        // ивритской локали), parts[1] — дисциплина, остальное — название соревнования.
        var discipline = parts.Count > 1 ? parts[1] : string.Empty;
        var competition = parts.Count > 2 ? string.Join(" - ", parts.Skip(2)) : string.Empty;
        return (competition, date, discipline);
    }

    // ── Сетка заплывов дня ───────────────────────────────────────────────────

    /// <summary>
    /// Разбирает <c>AthleticsDisciplines/{logligId}</c> — программу соревнования.
    ///
    /// id заплыва берётся из ЛЮБОЙ из четырёх кнопок строки: у предстоящего старта кнопок
    /// результатов ещё нет, и опираться только на <c>AthleticsDisciplineResults</c>
    /// (как это делает разбор результатов) значило бы не увидеть ни одного заплыва там,
    /// где фича и нужна.
    /// </summary>
    public static IReadOnlyList<LogligDisciplineGridRow> ParseDisciplineGrid(string html)
    {
        var rows = new List<LogligDisciplineGridRow>();
        var seen = new HashSet<int>();

        foreach (var rowHtml in TopLevelRows(html))
        {
            var idMatch = DisciplineLinkRx().Match(rowHtml);
            if (!idMatch.Success) continue;
            if (!int.TryParse(idMatch.Groups[1].Value, out var disciplineId)) continue;
            if (!seen.Add(disciplineId)) continue;   // четыре кнопки на строку — id один

            var cells = CellsOf(rowHtml);
            if (cells.Count < 7) continue;

            var disciplineRaw = cells[1];
            var category = cells[2];
            var (style, distance, isRelay) = ParseDiscipline(disciplineRaw);
            var (gender, ageBand) = ParseCategory(category);

            rows.Add(new LogligDisciplineGridRow(
                disciplineId,
                ParseInt(cells[0]),
                disciplineRaw,
                category,
                style,
                distance,
                gender,
                ageBand,
                isRelay,
                NormalizeSeedTime(cells[3]),
                ParseStartAt(cells[4]),
                ParseInt(cells[5]) ?? 0,
                ParseInt(cells[6]) ?? 0));
        }

        return rows;
    }

    // ── Общие разборы ────────────────────────────────────────────────────────

    /// <summary>
    /// «100 מעורב אישי» → комплекс + «100»; «4X50 חופשי שליחים» → вольный + «4X50» + эстафета.
    /// Слово «שליחים» снимается до определения стиля: иначе оно участвует в поиске по токенам.
    /// </summary>
    private static (string Style, string Distance, bool IsRelay) ParseDiscipline(string disciplineRaw)
    {
        if (string.IsNullOrWhiteSpace(disciplineRaw)) return (string.Empty, string.Empty, false);

        var isRelay = disciplineRaw.Contains(RelayMarker, StringComparison.Ordinal);
        var distanceMatch = DistanceRx().Match(disciplineRaw);
        var distance = distanceMatch.Success ? distanceMatch.Value.ToUpperInvariant() : string.Empty;

        var styleText = distanceMatch.Success
            ? disciplineRaw[(distanceMatch.Index + distanceMatch.Length)..]
            : disciplineRaw;
        styleText = styleText.Replace(RelayMarker, string.Empty, StringComparison.Ordinal).Trim();

        // «4X50 שליחים» без названия стиля — у источника это вольная эстафета.
        var style = styleText.Length == 0 && isRelay
            ? HebrewTextHelper.NormalizeStyleName("freestyle")
            : HebrewTextHelper.ResolveStyle(styleText);

        return (style, distance, isRelay);
    }

    /// <summary>«בנות 10» → female + «10»; «בנים 8-9» → male + «8-9».</summary>
    private static (string Gender, string AgeBand) ParseCategory(string category)
    {
        var tokens = category.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var gender = HebrewTextHelper.NormalizeGenderHE(tokens.FirstOrDefault() ?? string.Empty);
        var age = string.Join(' ', tokens.Skip(1)).Trim();
        return (gender, age);
    }

    /// <summary>Подзаголовок секции с раундом: «גמר ישיר» / «גמר» / «מוקדמות».</summary>
    private static bool LooksLikeRound(string text) =>
        text.Contains("גמר", StringComparison.Ordinal) || text.Contains("מוקדמות", StringComparison.Ordinal);

    private static string RoundOf(string caption) =>
        caption.Contains("מוקדמות", StringComparison.Ordinal) ? LogligRounds.Prelim
        : caption.Contains("ישיר", StringComparison.Ordinal) ? LogligRounds.TimedFinal
        : LogligRounds.Final;

    /// <summary>
    /// «מקצה: 2 שעת הזנקה:10:09» → 10:09. Без времени в подписи — null.
    ///
    /// ⚠ Полночь — это НЕ время старта, а способ источника сказать «время заплыву не
    /// назначено»: та же условность, что <c>00:00.00</c> в графе норматива. Встречено вживую
    /// (соревнование 14208, заплыв 76324: «שעת הזנקה:00:00» при времени события 10:00).
    /// Принять её за настоящее время значит показать родителю «ребёнок плывёт в полночь»;
    /// вместо этого вызывающий откатится ко времени всего события.
    /// </summary>
    private static TimeOnly? ParseHeatTime(string caption)
    {
        var m = ClockRx().Match(caption);
        if (!m.Success) return null;
        if (!TimeOnly.TryParseExact(
                m.Value, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return null;

        return t == TimeOnly.MinValue ? null : t;
    }

    /// <summary>
    /// «19/02/2026 10:06:00» → местная дата-время (<c>Kind=Unspecified</c>). Пусто — null:
    /// у заплыва, которому ещё не назначили время, эта графа пустая, и это норма.
    /// </summary>
    private static DateTime? ParseStartAt(string raw) =>
        DateTime.TryParseExact(
            raw.Trim(), "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified)
            : null;

    /// <summary>
    /// «01:42.72» → как есть; «NT», «00:00.00» и пусто → null.
    ///
    /// «00:00.00» отдельно: источник печатает им «норматива нет» в сетке заплывов, и принять
    /// его за настоящее время значит завести пловца, проплывшего дистанцию за ноль секунд.
    /// </summary>
    private static string? NormalizeSeedTime(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0) return null;
        if (value.Equals(NoTimeMarker, StringComparison.OrdinalIgnoreCase)) return null;
        if (!TimeRx().IsMatch(value)) return null;
        return ZeroTimeRx().IsMatch(value) ? null : value;
    }

    /// <summary>
    /// Разделитель полей в заголовке — тире С ПРОБЕЛАМИ (см. <see cref="ParseStartListTitle"/>).
    /// </summary>
    private static IEnumerable<string> SplitOnDash(string text) =>
        DashRx().Split(text).Select(p => p.Trim()).Where(p => p.Length > 0);

    /// <summary>
    /// Строки таблицы ВЕРХНЕГО уровня — с учётом вложенности <c>&lt;tr&gt;</c>.
    ///
    /// Почему не регексом <c>&lt;tr&gt;(.*?)&lt;/tr&gt;</c>, как в разборе результатов. В ячейке
    /// норматива сетки заплывов сидит всплывающая подсказка — ЦЕЛАЯ таблица со своими
    /// строками. Нежадный регекс обрывает строку заплыва на закрывающем теге этой подсказки,
    /// и всё, что идёт дальше (в том числе ссылки на стартовый протокол), теряется:
    /// на чемпионате loglig 13627 так пропадало 8 заплывов из 177 — вместе с первым днём,
    /// у которого норматив проставлен у всех.
    /// </summary>
    private static IEnumerable<string> TopLevelRows(string html)
    {
        var i = 0;
        while (true)
        {
            var open = html.IndexOf("<tr", i, StringComparison.OrdinalIgnoreCase);
            if (open < 0) yield break;

            var contentStart = html.IndexOf('>', open);
            if (contentStart < 0) yield break;
            contentStart++;

            var depth = 1;
            var j = contentStart;
            var closeAt = -1;
            while (j < html.Length)
            {
                var nextOpen = html.IndexOf("<tr", j, StringComparison.OrdinalIgnoreCase);
                var nextClose = html.IndexOf("</tr", j, StringComparison.OrdinalIgnoreCase);
                if (nextClose < 0) break;

                if (nextOpen >= 0 && nextOpen < nextClose)
                {
                    depth++;
                    j = nextOpen + 3;
                    continue;
                }

                depth--;
                j = nextClose + 4;
                if (depth == 0) { closeAt = nextClose; break; }
            }

            if (closeAt < 0) yield break;   // незакрытая строка — дальше разбирать нечего
            yield return html[contentStart..closeAt];
            i = j;
        }
    }

    /// <summary>
    /// Ячейки строки. Вложенные таблицы (та же подсказка норматива) снимаются ДО разбора:
    /// их <c>&lt;td&gt;</c> иначе встают в общий ряд и сдвигают позиционный разбор колонок.
    /// Снимаются изнутри наружу — регекс матчит таблицу, внутри которой нет другой таблицы.
    /// </summary>
    private static List<string> CellsOf(string rowHtml)
    {
        var cleaned = rowHtml;
        for (var guard = 0; guard < 5 && InnerTableRx().IsMatch(cleaned); guard++)
            cleaned = InnerTableRx().Replace(cleaned, " ");

        return CellRx().Matches(cleaned).Select(m => Clean(m.Groups[1].Value)).ToList();
    }

    private static int? ParseInt(string s) => int.TryParse(s.Replace(",", string.Empty), out var v) ? v : null;

    /// <summary>Текст ячейки: без тегов, без html-сущностей, схлопнутые пробелы.</summary>
    private static string Clean(string html) =>
        SpaceRx().Replace(WebUtility.HtmlDecode(TagRx().Replace(html, " ")), " ").Trim();

    private const string RelayMarker = "שליחים";
    private const string NoTimeMarker = "NT";
    private const string HeatColumnHeader = "מקצה";
    private const string BirthYearHeader = "שנת לידה";

    [GeneratedRegex("""<h4[^>]*class="disciplines-title"[^>]*>(.*?)</h4>""", RegexOptions.Singleline)]
    private static partial Regex TitleRx();

    [GeneratedRegex("""<t[dh][^>]*>(.*?)</t[dh]>""", RegexOptions.Singleline)]
    private static partial Regex CellRx();

    /// <summary>Самая внутренняя таблица: та, внутри которой нет открывающего тега таблицы.</summary>
    [GeneratedRegex("""<table(?:(?!<table)[\s\S])*?</table>""", RegexOptions.IgnoreCase)]
    private static partial Regex InnerTableRx();

    [GeneratedRegex("""<a[^>]*href="/Players/Details/(\d+)""", RegexOptions.Singleline)]
    private static partial Regex PlayerLinkRx();

    /// <summary>
    /// id заплыва из любой из четырёх кнопок строки. Требуется префикс <c>/LeagueTable/</c>
    /// и слэш перед числом: иначе кнопка «весь стартовый протокол»
    /// (<c>GenerateSwimmingAllStartList?competitionId=14208</c>) из шапки таблицы прошла бы
    /// за заплыв с id соревнования.
    /// </summary>
    [GeneratedRegex(
        """/LeagueTable/(?:StartList|RegisteredCompetitionAthletes|AthleticsDisciplineResultsByHeat|AthleticsDisciplineResults)/(\d+)""")]
    private static partial Regex DisciplineLinkRx();

    /// <summary>Подпись заплыва: «מקצה:» с номером — отличает её от колонки «מקצה».</summary>
    [GeneratedRegex("""מקצה:\s*\d+""")]
    private static partial Regex HeatCaptionRx();

    [GeneratedRegex("""\d{1,2}:\d{2}""")]
    private static partial Regex ClockRx();

    [GeneratedRegex("""^\d+[xX]\d+|^\d+""")]
    private static partial Regex DistanceRx();

    [GeneratedRegex("""^\d{1,2}:\d{2}\.\d{2}$""")]
    private static partial Regex TimeRx();

    [GeneratedRegex("""^0+:0+\.0+$""")]
    private static partial Regex ZeroTimeRx();

    [GeneratedRegex("""\d{2}/\d{2}/\d{4}""")]
    private static partial Regex DateRx();

    [GeneratedRegex("""\s+-\s+""")]
    private static partial Regex DashRx();

    [GeneratedRegex("""<[^>]+>""", RegexOptions.Singleline)]
    private static partial Regex TagRx();

    [GeneratedRegex("""\s+""")]
    private static partial Regex SpaceRx();
}
