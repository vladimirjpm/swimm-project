using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Parsing.Discovery;

/// <summary>
/// Автозабор соревнований с isr.org.il (фаза 6): список competitions.asp, детальная comp.asp
/// (площадка + loglig-id результатов), PDF-протокол через loglig ExportSwimmingCompetitionResults.
/// PDF-ов на самом isr.org.il больше нет — результаты публикуются в iframe loglig.com,
/// а экспорт оттуда отдаёт тот же PDF-протокол, который понимает IsrOrgParser (проверено
/// вживую 2026-07-15: /Leagues/ExportSwimmingCompetitionResults?competitionId=… → PDF 1.4).
/// Вежливость: пауза между запросами; сеть только здесь, парсинг — чистые статические методы
/// (тесты гоняют их на HTML-снапшотах из Fixtures/Discovery, в сеть не ходят).
/// При 0 распознанных строк — ЯВНАЯ ошибка «вёрстка изменилась», не тихий ноль (B4).
/// </summary>
public partial class IsrOrgDiscoveryProvider : ICompetitionDiscoveryProvider
{
    private const string ListUrl = "https://isr.org.il/competitions.asp";
    private const string DetailsUrl = "https://isr.org.il/comp.asp?compID=";
    private const string PdfUrlTemplate =
        "https://loglig.com/Leagues/ExportSwimmingCompetitionResults?competitionId={0}&culture={1}&isSplitResults=false&isByHeat=false";

    /// <summary>Пауза между последовательными запросами к чужому проду.</summary>
    private static readonly TimeSpan PoliteDelay = TimeSpan.FromSeconds(2);
    private static DateTime _lastRequestUtc = DateTime.MinValue;
    private static readonly SemaphoreSlim ThrottleLock = new(1, 1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public IsrOrgDiscoveryProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<DiscoveredListItem>> FetchListAsync(bool finished, CancellationToken ct = default)
    {
        // Без cYear сайт отдаёт текущий сезон; isFinish разделяет прошедшие/будущие.
        var url = $"{ListUrl}?cMonth=0&cType=0&cMode=0&isFinish={(finished ? "true" : "false")}";
        var html = await GetStringAsync(url, $"list-{(finished ? "finished" : "upcoming")}", ct);
        var items = ParseList(html);
        if (items.Count == 0 && finished)
            throw new InvalidOperationException(
                "isr.org.il/competitions.asp: не распознано ни одной строки — вёрстка сайта изменилась? " +
                "Проверь снапшот (Discovery:SnapshotDir) и регэкспы IsrOrgDiscoveryProvider.");
        return items;
    }

    public async Task<DiscoveredDetails> FetchDetailsAsync(int orgCompId, CancellationToken ct = default)
    {
        var html = await GetStringAsync(DetailsUrl + orgCompId, $"comp-{orgCompId}", ct);
        return ParseDetails(html);
    }

    public async Task<byte[]> FetchResultsPdfAsync(int logligId, string culture = "he-IL", CancellationToken ct = default)
    {
        var url = string.Format(CultureInfo.InvariantCulture, PdfUrlTemplate, logligId, culture);
        // loglig игнорирует culture в query string — язык протокола выбирается ТОЛЬКО
        // по куке _culture (классическая ASP.NET MVC culture-кука; выяснено 2026-07-18:
        // curl без куки отдаёт иврит на любой culture=, браузер Влада с кукой — английский).
        var bytes = await GetBytesAsync(url, $"results-{logligId}-{culture}.pdf", ct, cookie: $"_culture={culture}");
        // PDF начинается с %PDF — HTML-страница ошибки loglig не должна уйти в парсер молча.
        if (bytes.Length < 4 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F')
            throw new InvalidOperationException(
                $"loglig вернул не PDF для competitionId={logligId} (результаты ещё не опубликованы или экспорт изменился).");
        return bytes;
    }

    // ── Парсинг (чистые функции, тестируются на снапшотах) ──────────────────

    [GeneratedRegex(
        """<td class="c-date">([^<]+)</td>\s*<td class="rr c-name"><a[^>]*href="comp\.asp\?compID=(\d+)"[^>]*>([^<]*)</a>""",
        RegexOptions.Singleline)]
    private static partial Regex ListRowRx();

    [GeneratedRegex("""<h1 class="comp_title">(.*?)</h1>""", RegexOptions.Singleline)]
    private static partial Regex TitleRx();

    [GeneratedRegex("""alt="מקום התחרות"\s*/>\s*<br\s*/>([^<]*)<""", RegexOptions.Singleline)]
    private static partial Regex VenueRx();

    [GeneratedRegex("""LeagueTable/AthleticsDisciplines/(\d+)""")]
    private static partial Regex LogligRx();

    [GeneratedRegex("""href="#day-\d+""")]
    private static partial Regex DayTabRx();

    public static IReadOnlyList<DiscoveredListItem> ParseList(string html)
    {
        var items = new List<DiscoveredListItem>();
        foreach (Match m in ListRowRx().Matches(html))
        {
            if (!TryParseDateRange(m.Groups[1].Value.Trim(), out var start, out var end))
                continue; // строка с нераспознанной датой не валит весь список — её видно по счётчику
            var name = WebUtility.HtmlDecode(m.Groups[3].Value).Trim();
            if (name.Length == 0) continue;
            items.Add(new DiscoveredListItem(int.Parse(m.Groups[2].Value), name, start, end));
        }
        return items;
    }

    public static DiscoveredDetails ParseDetails(string html)
    {
        var title = TitleRx().Match(html);
        if (!title.Success)
            throw new InvalidOperationException(
                "isr.org.il/comp.asp: заголовок comp_title не найден — вёрстка изменилась?");

        var venueMatch = VenueRx().Match(html);
        var venue = venueMatch.Success ? WebUtility.HtmlDecode(venueMatch.Groups[1].Value).Trim() : null;
        var loglig = LogligRx().Match(html);
        var dayCount = DayTabRx().Matches(html).Count;

        return new DiscoveredDetails(
            WebUtility.HtmlDecode(title.Groups[1].Value).Trim(),
            string.IsNullOrEmpty(venue) ? null : venue,
            loglig.Success ? int.Parse(loglig.Groups[1].Value) : null,
            Math.Max(dayCount, 1));
    }

    /// <summary>
    /// Даты списка: «10.10.2025», «5.6.2026», диапазоны «19-20.6.2026» и «28.6-2.7.2026».
    /// Недостающие месяц/год начала берутся из конца диапазона.
    /// </summary>
    public static bool TryParseDateRange(string raw, out DateTime start, out DateTime end)
    {
        start = end = default;
        var parts = raw.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2) return false;

        if (!TryParseDayMonthYear(parts[^1], out var endDay, out var endMonth, out var endYear))
            return false;
        end = new DateTime(endYear, endMonth, endDay, 0, 0, 0, DateTimeKind.Utc);

        if (parts.Length == 1)
        {
            start = end;
            return true;
        }

        var seg = parts[0].Split('.', StringSplitOptions.TrimEntries);
        try
        {
            start = seg.Length switch
            {
                1 => new DateTime(endYear, endMonth, int.Parse(seg[0]), 0, 0, 0, DateTimeKind.Utc),
                2 => new DateTime(endYear, int.Parse(seg[1]), int.Parse(seg[0]), 0, 0, 0, DateTimeKind.Utc),
                3 => new DateTime(int.Parse(seg[2]), int.Parse(seg[1]), int.Parse(seg[0]), 0, 0, 0, DateTimeKind.Utc),
                _ => default
            };
        }
        catch (FormatException) { return false; }
        catch (ArgumentOutOfRangeException) { return false; }

        return start != default && start <= end;
    }

    private static bool TryParseDayMonthYear(string s, out int day, out int month, out int year)
    {
        day = month = year = 0;
        var seg = s.Split('.', StringSplitOptions.TrimEntries);
        return seg.Length == 3
            && int.TryParse(seg[0], out day) && int.TryParse(seg[1], out month) && int.TryParse(seg[2], out year)
            && day is >= 1 and <= 31 && month is >= 1 and <= 12 && year is >= 2000 and <= 2100;
    }

    // ── Сеть ─────────────────────────────────────────────────────────────────

    private async Task<string> GetStringAsync(string url, string snapshotName, CancellationToken ct)
    {
        var bytes = await GetBytesAsync(url, snapshotName + ".html", ct);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]> GetBytesAsync(string url, string snapshotName, CancellationToken ct, string? cookie = null)
    {
        var uri = new Uri(url);
        if (!IsAllowedHost(uri.Host))
            throw new InvalidOperationException($"Домен '{uri.Host}' не в whitelist автозабора.");

        await ThrottleAsync(ct);

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SwimmBot/1.0 (+swimm-project discovery)");
        if (cookie != null)
            client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await client.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);

        SaveSnapshot(snapshotName, bytes);
        return bytes;
    }

    private static bool IsAllowedHost(string host) =>
        string.Equals(host, "isr.org.il", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".isr.org.il", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "loglig.com", StringComparison.OrdinalIgnoreCase);

    private static async Task ThrottleAsync(CancellationToken ct)
    {
        await ThrottleLock.WaitAsync(ct);
        try
        {
            var wait = _lastRequestUtc + PoliteDelay - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);
            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            ThrottleLock.Release();
        }
    }

    /// <summary>Снапшот скачанного на диск для отладки (B4). Пусто в конфиге — не сохраняем.</summary>
    private void SaveSnapshot(string name, byte[] bytes)
    {
        var dir = _configuration["Discovery:SnapshotDir"];
        if (string.IsNullOrWhiteSpace(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{name}"), bytes);
        }
        catch
        {
            // Снапшот — best-effort диагностика, сбой записи не должен ломать забор.
        }
    }
}
