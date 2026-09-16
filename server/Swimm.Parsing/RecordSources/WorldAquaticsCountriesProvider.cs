using System.Text.Json;
using System.Text.Json.Serialization;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Список стран api.worldaquatics.com (этап 11.1.1) — из него батч-прогон берёт, чьи
/// национальные рекорды качать. Один запрос на прогон, в БД не сохраняется.
///
/// Разведка 2026-09-15 и повторный замер 16.09: <c>GET /fina/countries</c> отдаёт 246
/// записей, из них 11 — не страны (см. <see cref="ParseCountries"/>), остаётся
/// <b>235 реальных</b> → 470 NR-файлов на полный прогон.
/// </summary>
public class WorldAquaticsCountriesProvider : IRecordCountriesProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public WorldAquaticsCountriesProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string Source => "worldrecords";

    public async Task<IReadOnlyList<RecordCountryDto>> GetCountriesAsync(CancellationToken ct = default)
    {
        // Список маленький (≈80 КБ) и отдаётся за секунды — отдельный таймаут от отчётов.
        var client = WorldAquaticsSource.CreateClient(_httpClientFactory, TimeSpan.FromSeconds(60));

        var response = await client.GetAsync(WorldAquaticsSource.CountriesUrl, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);

        return ParseCountries(json);
    }

    /// <summary>
    /// Чистая функция разбора: JSON → страны. Отдельно от сети, чтобы тест ходил по
    /// зафиксированному фрагменту ответа, а не в интернет (образец —
    /// <see cref="IsrOrgRecordsPageResolver.ParseLinks"/>).
    ///
    /// Отсеиваем по трём признакам, каждый — не вкусовщина:
    /// 1. <c>RegionName</c> пуст → это не страна, а псевдо-сборная: AIN, CLB, EOR, FINA,
    ///    IOA, IOC, IOP, NAA, NAB, NAC, SMF (все 11 — с пустым регионом, других отличий нет).
    /// 2. код не из трёх букв → у всех 235 реальных стран ровно alpha-3, четырёхбуквенный
    ///    <c>FINA</c> — как раз псевдо-сборная. Код уедет в <c>Record.RegionCode</c>, а там
    ///    длина — часть контракта.
    /// 3. <c>Id</c> не разбирается как GUID → в URL отчёта пойдёт только распознанный GUID,
    ///    строкой из ответа мы query не склеиваем.
    /// </summary>
    public static IReadOnlyList<RecordCountryDto> ParseCountries(string json)
    {
        var raw = JsonSerializer.Deserialize<List<ApiCountry>>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Список стран источника не разобрался: пустой ответ.");

        var countries = new List<RecordCountryDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in raw)
        {
            if (string.IsNullOrWhiteSpace(c.RegionName)) continue;

            var code = (c.Code ?? "").Trim().ToUpperInvariant();
            if (code.Length != 3 || !code.All(char.IsAsciiLetter)) continue;

            if (!Guid.TryParse(c.Id, out var id)) continue;

            // Дублей кодов в ответе нет (проверено 16.09), но ключ прогона обязан быть
            // уникальным: иначе одна страна качалась бы дважды и второй ответ молча
            // переписал бы первый.
            if (!seen.Add(code)) continue;

            countries.Add(new RecordCountryDto(
                Code: code,
                SourceId: id.ToString(),
                Name: (c.Name ?? "").Trim(),
                Region: c.RegionName!.Trim()));
        }

        if (countries.Count == 0)
            throw new InvalidOperationException(
                "Список стран источника не разобрался: ни одной страны с непустым регионом.");

        // Порядок прогона детерминированный — по нему же читается отчёт «страна N из 235».
        countries.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));
        return countries;
    }

    /// <summary>Запись ответа <c>/fina/countries</c>; лишние поля (флаги, RegionId) не нужны.</summary>
    private sealed class ApiCountry
    {
        [JsonPropertyName("Id")] public string? Id { get; set; }
        [JsonPropertyName("Name")] public string? Name { get; set; }
        [JsonPropertyName("Code")] public string? Code { get; set; }
        [JsonPropertyName("RegionName")] public string? RegionName { get; set; }
    }
}
