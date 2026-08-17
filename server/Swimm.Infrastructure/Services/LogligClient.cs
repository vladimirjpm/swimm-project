using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Клиент карточки игрока loglig.com (docs/loglig-id-plan.md, шаг 2). Анти-SSRF: принимает
/// только int logligId, URL собирается из константы <see cref="BaseUrl"/> внутри клиента —
/// методов, принимающих произвольный URL, нет. Парсинг — чистые статические регексы в стиле
/// IsrOrgDiscoveryProvider, тестируются на фикстуре без сети; сырой HTML никуда не сохраняется.
/// </summary>
public partial class LogligClient : ILogligClient
{
    private const string BaseUrl = "https://loglig.com:2053";

    /// <summary>Без валидного seasonId карточка отдаёт 500; старый сезон — урезанную таблицу
    /// результатов. Значение сезона меняется раз в год — переопределяется конфигом Loglig:SeasonId.</summary>
    private const int DefaultSeasonId = 1715; // сезон 2025/26

    /// <summary>Какую долю голосов должно набрать значение очков, чтобы попасть в шкалу.</summary>
    private const int ScaleVoteThresholdPercent = 60;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LogligClient> _logger;
    private readonly int _seasonId;

    public LogligClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<LogligClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _seasonId = configuration.GetValue("Loglig:SeasonId", DefaultSeasonId);
    }

    public string BuildPublicProfileUrl(int logligId) =>
        $"{BaseUrl}/Players/Details/{logligId}?seasonId={_seasonId}";

    public async Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, CancellationToken ct = default)
    {
        var url = BuildPublicProfileUrl(logligId);
        try
        {
            var client = _httpClientFactory.CreateClient("loglig");
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "loglig: карточка игрока {LogligId} недоступна, статус {StatusCode}",
                    logligId, (int)response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            var card = ParseCard(html);
            if (card is null)
                _logger.LogWarning("loglig: карточка игрока {LogligId} не распозналась (вёрстка изменилась?)", logligId);
            return card;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "loglig: ошибка запроса карточки игрока {LogligId}", logligId);
            return null;
        }
    }

    // ── Официальный клубный зачёт соревнования ─────────────────────────────────

    /// <summary>
    /// Есть ли у соревнования опубликованный клубный зачёт («דירוג מועדונים»), и по какой
    /// шкале он посчитан.
    ///
    /// Кнопка зачёта есть в разметке у всех соревнований — это шаблон; признак публикации
    /// один: непустая таблица от <c>LoadClubStandingSwimmingPoints</c>. Шкала снимается
    /// отдельно, с колонки «ניקוד קבוצתי» индивидуальных заплывов: официальную шкалу нигде
    /// не отдают машинно, а в регламенте (PDF) она бывает картинкой.
    /// </summary>
    /// <returns>null — соревнование недоступно (сеть/404), это НЕ то же самое, что «зачёта нет».</returns>
    public async Task<LogligCompetitionStanding?> GetCompetitionStandingAsync(
        int logligId, int scaleSampleEvents = 12, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("loglig");

        var pageUrl = $"{BaseUrl}/LeagueTable/AthleticsDisciplines/{logligId}";
        string page;
        try
        {
            var response = await client.GetAsync(pageUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("loglig: страница соревнования {LogligId} недоступна, статус {StatusCode}",
                    logligId, (int)response.StatusCode);
                return null;
            }
            page = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "loglig: ошибка запроса страницы соревнования {LogligId}", logligId);
            return null;
        }

        // seasonId берём со страницы, а не из конфига: у прошлых сезонов он свой, а с чужим
        // таблица приходит пустой — и «зачёта нет» стало бы ложным выводом.
        var seasonId = ParseSeasonId(page) ?? _seasonId;

        string standingHtml;
        try
        {
            var url = $"{BaseUrl}/LeagueTable/LoadClubStandingSwimmingPoints/{logligId}?seasonId={seasonId}";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("loglig: клубный зачёт {LogligId} недоступен, статус {StatusCode}",
                    logligId, (int)response.StatusCode);
                return null;
            }
            standingHtml = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "loglig: ошибка запроса клубного зачёта {LogligId}", logligId);
            return null;
        }

        if (!HasStandingRows(standingHtml))
            return new LogligCompetitionStanding(false, new Dictionary<int, int>());

        // Шкала: по нескольким заплывам, чтобы одиночный мелкий заплыв (5 участников)
        // не выдал огрызок из пяти мест.
        var eventIds = ParseEventIds(page).Take(Math.Max(1, scaleSampleEvents) * 2).ToList();
        var votes = new Dictionary<int, Dictionary<int, int>>();
        var used = 0;

        foreach (var eventId in eventIds)
        {
            if (used >= scaleSampleEvents) break;
            string eventHtml;
            try
            {
                var url = $"{BaseUrl}/LeagueTable/AthleticsDisciplineResults/{eventId}?isModal=True&showCategories=True";
                var response = await client.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) continue;
                eventHtml = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "loglig: ошибка запроса заплыва {EventId}", eventId);
                continue;
            }

            // Эстафеты дают ДВОЙНЫЕ очки — попав в выборку, они удвоили бы всю шкалу.
            if (IsRelayEventPage(eventHtml)) continue;

            var rows = ParseEventClubPoints(eventHtml);
            if (rows.Count == 0) continue;

            used++;
            foreach (var (place, points) in rows)
            {
                if (!votes.TryGetValue(place, out var byPoints))
                    votes[place] = byPoints = new Dictionary<int, int>();
                byPoints[points] = byPoints.GetValueOrDefault(points) + 1;
            }
        }

        // Мода по каждому месту — но только там, где наблюдения СОГЛАСНЫ: ties и делёж мест
        // дают разнобой, и «победившее большинством в 30%» значение — угадайка. Место, по
        // которому уверенности нет, в шкалу не попадает: лучше короткая честная шкала, чем
        // длинная выдуманная (правило подбирается по точному совпадению).
        var scale = new Dictionary<int, int>();
        foreach (var (place, byPoints) in votes)
        {
            var total = byPoints.Values.Sum();
            var (points, count) = byPoints.MaxBy(p => p.Value);
            if (count < 2 || count * 100 < total * ScaleVoteThresholdPercent) continue;
            scale[place] = points;
        }

        return new LogligCompetitionStanding(true, scale);
    }

    // ── Парсинг (чистые функции, тестируются на фикстуре) ──────────────────────

    [GeneratedRegex("""<div class="pld-hero-top">.*?<h1>([^<]*)</h1>""", RegexOptions.Singleline)]
    private static partial Regex NameRx();

    [GeneratedRegex(
        """<span class="pld-chip-label">שנת לידה</span>\s*(\d+)\s*</span>""",
        RegexOptions.Singleline)]
    private static partial Regex BirthYearRx();

    [GeneratedRegex(
        """<span class="pld-chip-label">מגדר</span>\s*([^<]*?)\s*</span>""",
        RegexOptions.Singleline)]
    private static partial Regex GenderRx();

    [GeneratedRegex(
        """<span class="pld-chip-label">אגודה</span>\s*([^<]*?)\s*</span>""",
        RegexOptions.Singleline)]
    private static partial Regex ClubRx();

    [GeneratedRegex(
        """<table[^>]*id="pld-pb-table"[^>]*>(.*?)</table>""",
        RegexOptions.Singleline)]
    private static partial Regex PbTableRx();

    [GeneratedRegex(
        """<tr class="pld-pb-row"[^>]*>\s*<td[^>]*>([^<]*)</td>\s*<td>([^<]*)</td>\s*<td>([^<]*)</td>\s*<td>([^<]*)</td>\s*<td[^>]*>([^<]*)</td>""",
        RegexOptions.Singleline)]
    private static partial Regex ResultRowRx();

    [GeneratedRegex("""4[xX]\d+|שליחים""")]
    private static partial Regex RelayMarkerRx();

    // ── Регексы клубного зачёта соревнования ──────────────────────────────────

    [GeneratedRegex("""seasonId=(\d+)""")]
    private static partial Regex SeasonIdRx();

    [GeneratedRegex("""AthleticsDisciplineResults/(\d+)""")]
    private static partial Regex EventIdRx();

    [GeneratedRegex("""<tr[^>]*>(.*?)</tr>""", RegexOptions.Singleline)]
    private static partial Regex TableRowRx();

    [GeneratedRegex("""<t[dh][^>]*>(.*?)</t[dh]>""", RegexOptions.Singleline)]
    private static partial Regex TableCellRx();

    [GeneratedRegex("""<[^>]+>""", RegexOptions.Singleline)]
    private static partial Regex TagRx();

    /// <summary>Заголовок заплыва (до первой таблицы) — по нему отличаем эстафету.</summary>
    [GeneratedRegex("""<h[1-6][^>]*>(.*?)</h[1-6]>""", RegexOptions.Singleline)]
    private static partial Regex HeadingRx();

    /// <summary>seasonId соревнования — из ajax-URL, который страница подставляет сама.</summary>
    internal static int? ParseSeasonId(string html)
    {
        var m = SeasonIdRx().Match(html);
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>Id заплывов соревнования в порядке появления, без повторов.</summary>
    internal static IReadOnlyList<int> ParseEventIds(string html) =>
        EventIdRx().Matches(html)
            .Select(m => int.TryParse(m.Groups[1].Value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

    /// <summary>
    /// Таблица клубного зачёта непуста. Пустой ответ — это ровно
    /// <c>&lt;div class="marginTop20"&gt;</c> без единой строки.
    /// </summary>
    internal static bool HasStandingRows(string html) =>
        TableRowRx().Matches(html).Any(row => Cells(row.Groups[1].Value) is { Count: 3 } c && int.TryParse(c[0], out _));

    /// <summary>Заплыв эстафетный — очки в нём двойные, для снятия шкалы он не годится.</summary>
    internal static bool IsRelayEventPage(string html)
    {
        var heading = HeadingRx().Matches(html)
            .Select(m => WebUtility.HtmlDecode(TagRx().Replace(m.Groups[1].Value, " ")))
            .FirstOrDefault(t => t.Contains("תוצאות"));
        return heading != null && RelayMarkerRx().IsMatch(heading);
    }

    /// <summary>Заголовок колонки клубных очков в таблице заплыва.</summary>
    private const string ClubPointsHeader = "ניקוד קבוצתי";

    /// <summary>
    /// Пары «зачётное место → клубные очки» из таблицы результатов заплыва.
    ///
    /// Две ловушки вёрстки, из-за которых наивное чтение даёт мусорную шкалу:
    ///
    /// 1. Колонку клубных очков ищем ПО ЗАГОЛОВКУ, а не с конца строки: у части строк её нет
    ///    вовсе, и «последняя ячейка» оказывается очками FINA (сотни).
    /// 2. Место берём не из колонки «מיקום», а считаем порядок ЗАЧЁТНЫХ строк внутри
    ///    возрастной категории. Очки идут по зачётному рангу: на Маккабиаде иностранки в
    ///    клубный зачёт не входят, и участница с протокольным 5-м местом получает очки
    ///    второго. По колонке места шкала выходила «5 → 11», по зачётному рангу — «2 → 11».
    ///
    /// Категории внутри заплыва разделены строкой-заголовком («גמר ישיר - מאסטרס נ 21-29»),
    /// на ней счётчик сбрасывается.
    /// </summary>
    internal static IReadOnlyList<(int Place, int Points)> ParseEventClubPoints(string html)
    {
        var rows = new List<(int, int)>();

        int? pointsIndex = null;
        var scoringRank = 0;

        foreach (Match row in TableRowRx().Matches(html))
        {
            var cells = Cells(row.Groups[1].Value);

            if (pointsIndex is null)
            {
                var found = cells.FindIndex(c => c.Contains(ClubPointsHeader));
                if (found < 0) continue;
                pointsIndex = found;
                continue;
            }

            // Заголовок возрастной категории — одна-две ячейки на всю ширину.
            if (cells.Count(c => c.Length > 0) <= 2)
            {
                scoringRank = 0;
                continue;
            }

            if (cells.Count <= pointsIndex) continue;
            // Строка без клубных очков (снят, вне зачёта, иностранный клуб) ранга не занимает.
            if (!int.TryParse(cells[pointsIndex.Value], out var points) || points <= 0) continue;

            rows.Add((++scoringRank, points));
        }

        return rows;
    }

    /// <summary>
    /// Текст ячеек строки без разметки. Пустые ячейки СОХРАНЯЮТСЯ: в шапке таблицы заплыва
    /// есть безымянная колонка, и без неё индексы колонок съезжают.
    /// </summary>
    private static List<string> Cells(string rowHtml) =>
        TableCellRx().Matches(rowHtml)
            .Select(c => WebUtility.HtmlDecode(TagRx().Replace(c.Groups[1].Value, " ")).Trim())
            .Select(t => WhitespaceRx().Replace(t, " "))
            .ToList();

    [GeneratedRegex("""\s+""")]
    private static partial Regex WhitespaceRx();

    [GeneratedRegex("""^(\d+)""")]
    private static partial Regex LeadingDistanceRx();

    /// <summary>Парсит HTML карточки игрока. Null — не похоже на карточку (нет &lt;h1&gt;).</summary>
    public static LogligPlayerCard? ParseCard(string html)
    {
        var nameMatch = NameRx().Match(html);
        if (!nameMatch.Success)
            return null;

        var fullName = WebUtility.HtmlDecode(nameMatch.Groups[1].Value).Trim();
        if (fullName.Length == 0)
            return null;

        var birthYearMatch = BirthYearRx().Match(html);
        int? birthYear = birthYearMatch.Success && int.TryParse(birthYearMatch.Groups[1].Value, out var by) ? by : null;

        var genderMatch = GenderRx().Match(html);
        var gender = NormalizeGender(genderMatch.Success ? genderMatch.Groups[1].Value : null);

        var clubMatch = ClubRx().Match(html);
        var clubName = clubMatch.Success ? WebUtility.HtmlDecode(clubMatch.Groups[1].Value).Trim() : null;
        if (string.IsNullOrEmpty(clubName)) clubName = null;

        var results = new List<LogligResultRow>();
        var tableMatch = PbTableRx().Match(html);
        if (tableMatch.Success)
        {
            foreach (Match row in ResultRowRx().Matches(tableMatch.Groups[1].Value))
            {
                var eventRaw = WebUtility.HtmlDecode(row.Groups[1].Value).Trim();
                var poolLengthRaw = row.Groups[2].Value.Trim();
                var timeRaw = WebUtility.HtmlDecode(row.Groups[3].Value).Trim();
                var dateRaw = row.Groups[4].Value.Trim();
                var competitionName = WebUtility.HtmlDecode(row.Groups[5].Value).Trim();

                if (!TryParseDdMmYyyy(dateRaw, out var date))
                    continue; // строка с нераспознанной датой пропускается, а не валит весь парс

                var isRelay = RelayMarkerRx().IsMatch(eventRaw);
                var distanceMatch = LeadingDistanceRx().Match(eventRaw);
                string? distance = null;
                if (isRelay)
                {
                    // «4X50 חופשי שליחים» → дистанция «4X50» (первый токен)
                    var firstToken = eventRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (firstToken != null && firstToken.Contains('X', StringComparison.OrdinalIgnoreCase))
                        distance = firstToken.ToUpperInvariant();
                }
                else if (distanceMatch.Success)
                {
                    distance = distanceMatch.Groups[1].Value;
                }

                var styleName = isRelay ? null : MapStyleName(eventRaw);
                int.TryParse(poolLengthRaw, out var poolLength);

                results.Add(new LogligResultRow(
                    eventRaw,
                    distance,
                    styleName,
                    isRelay,
                    poolLength,
                    timeRaw,
                    ParseTimeToMilliseconds(timeRaw),
                    date,
                    competitionName));
            }
        }

        return new LogligPlayerCard(fullName, birthYear, gender, clubName, results);
    }

    /// <summary>נקבה → F, זכר → M, иначе null (формат как в Swimmer.Gender).</summary>
    internal static string? NormalizeGender(string? raw)
    {
        var value = raw?.Trim();
        return value switch
        {
            "נקבה" => "F",
            "זכר" => "M",
            _ => null
        };
    }

    /// <summary>Маппинг ивритского названия стиля (внутри EventRaw) → Style.Name нашей БД.</summary>
    internal static string? MapStyleName(string eventRaw)
    {
        if (eventRaw.Contains("מעורב אישי", StringComparison.Ordinal)) return "individual_medley";
        if (eventRaw.Contains("חופשי", StringComparison.Ordinal)) return "freestyle";
        if (eventRaw.Contains("גב", StringComparison.Ordinal)) return "backstroke";
        if (eventRaw.Contains("חזה", StringComparison.Ordinal)) return "breaststroke";
        if (eventRaw.Contains("פרפר", StringComparison.Ordinal)) return "butterfly";
        return null;
    }

    /// <summary>dd/MM/yyyy → DateTime (Kind=Utc). Невалидная строка → false.</summary>
    private static bool TryParseDdMmYyyy(string raw, out DateTime date)
    {
        if (DateTime.TryParseExact(
                raw, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        date = default;
        return false;
    }

    /// <summary>«MM:SS.ff» или «HH:MM:SS.ff» → миллисекунды. Невалидная строка → null.</summary>
    internal static int? ParseTimeToMilliseconds(string raw)
    {
        var parts = raw.Trim().Split(':');
        if (parts.Length is not (2 or 3))
            return null;

        var secParts = parts[^1].Split('.');
        if (secParts.Length != 2)
            return null;

        if (!int.TryParse(secParts[0], out var seconds)) return null;
        if (!int.TryParse(secParts[1], out var frac)) return null;
        if (secParts[1].Length != 2) return null; // «ff» — сотые доли секунды

        int hours = 0, minutes;
        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[0], out hours)) return null;
            if (!int.TryParse(parts[1], out minutes)) return null;
        }
        else
        {
            if (!int.TryParse(parts[0], out minutes)) return null;
        }

        if (seconds is < 0 or > 59 || minutes is < 0 or > 59 || hours < 0)
            return null;

        var totalMinutes = hours * 60 + minutes;
        return (totalMinutes * 60 + seconds) * 1000 + frac * 10;
    }
}
