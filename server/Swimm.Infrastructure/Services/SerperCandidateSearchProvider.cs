using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Поиск кандидатов Loglig ID через serper.dev (docs/loglig-id-plan.md, шаг 4). Google Custom
/// Search JSON API закрыт для новых клиентов с 01.2026 — используем serper (SERP-прокси, отдаёт
/// настоящую Google-выдачу). Анти-SSRF: из ссылок выдачи вынимаем только числовой ID
/// (Players/Details/{id}); сами ссылки нигде не фетчатся. Graceful: пустой/отсутствующий ApiKey —
/// провайдер отключён, отдаёт пустой список и один warning в лог (не на каждый вызов).
/// </summary>
public partial class SerperCandidateSearchProvider : ICandidateSearchProvider
{
    private const string SearchUrl = "https://google.serper.dev/search";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SerperCandidateSearchProvider> _logger;
    private readonly string? _apiKey;

    private static bool _warnedNotConfigured;

    public SerperCandidateSearchProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<SerperCandidateSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration.GetSection("CandidateSearch")["ApiKey"];
        if (string.IsNullOrWhiteSpace(_apiKey))
            _apiKey = null;
    }

    public bool IsConfigured => _apiKey is not null;

    public async Task<IReadOnlyList<int>> FindCandidatesAsync(
        string lastNameHe, string firstNameHe, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            if (!_warnedNotConfigured)
            {
                _logger.LogWarning("candidate-search: CandidateSearch:ApiKey не задан — поиск кандидатов отключён");
                _warnedNotConfigured = true;
            }
            return [];
        }

        var queries = BuildQueries(lastNameHe, firstNameHe);
        var candidates = new List<int>();

        foreach (var query in queries)
        {
            var found = await SearchOneAsync(query, ct);
            foreach (var id in found)
            {
                if (!candidates.Contains(id))
                    candidates.Add(id);
            }

            if (candidates.Count >= 1)
                break;
        }

        return candidates.Count > 5 ? candidates.Take(5).ToList() : candidates;
    }

    private async Task<IReadOnlyList<int>> SearchOneAsync(string query, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("serper");
            var body = JsonSerializer.Serialize(new { q = query, num = 10 });
            using var request = new HttpRequestMessage(HttpMethod.Post, SearchUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-API-KEY", _apiKey);

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "candidate-search: serper-запрос {Query} неуспешен, статус {StatusCode}",
                    query, (int)response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var ids = ExtractPlayerIds(json);
            _logger.LogDebug("candidate-search: запрос {Query} → {Count} кандидатов", query, ids.Count);
            return ids;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "candidate-search: ошибка serper-запроса {Query}", query);
            return [];
        }
    }

    // ── Парсинг/сборка запросов (чистые статические функции, тестируются без сети) ────────────

    [GeneratedRegex("""Players/Details/(\d+)""")]
    private static partial Regex PlayerIdRx();

    /// <summary>Парсит JSON выдачи serper, вынимает уникальные loglig ID (по появлению), максимум <paramref name="max"/>.</summary>
    internal static IReadOnlyList<int> ExtractPlayerIds(string serperJson, int max = 5)
    {
        var result = new List<int>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(serperJson);
        }
        catch (JsonException)
        {
            return result;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("organic", out var organic) ||
                organic.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in organic.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("link", out var linkProp) ||
                    linkProp.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var link = linkProp.GetString();
                if (string.IsNullOrEmpty(link))
                    continue;

                var match = PlayerIdRx().Match(link);
                if (!match.Success)
                    continue;

                if (!int.TryParse(match.Groups[1].Value, out var id))
                    continue;

                if (!result.Contains(id))
                    result.Add(id);

                if (result.Count >= max)
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Запросы для поиска пловца по имени: фамилия+имя, имя+фамилия, фолбэк — только фамилия.
    /// Пустые/пробельные части пропускаются (только фамилия → один запрос; обе пустые → ноль).
    /// </summary>
    internal static IReadOnlyList<string> BuildQueries(string lastNameHe, string firstNameHe)
    {
        var last = lastNameHe?.Trim() ?? "";
        var first = firstNameHe?.Trim() ?? "";

        if (last.Length == 0 && first.Length == 0)
            return [];

        if (last.Length == 0)
            return [$"site:loglig.com \"{first}\""];

        if (first.Length == 0)
            return [$"site:loglig.com \"{last}\""];

        return
        [
            $"site:loglig.com \"{last} {first}\"",
            $"site:loglig.com \"{first} {last}\"",
            $"site:loglig.com \"{last}\"",
        ];
    }
}
