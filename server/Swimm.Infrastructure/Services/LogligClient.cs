using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
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

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LogligClient> _logger;

    public LogligClient(IHttpClientFactory httpClientFactory, ILogger<LogligClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/Players/Details/{logligId}";
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
