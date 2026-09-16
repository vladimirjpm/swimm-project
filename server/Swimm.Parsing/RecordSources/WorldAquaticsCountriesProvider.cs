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
/// записей, из них 11 с пустым регионом и 20 расформированных/нейтральных
/// (см. <see cref="ParseCountries"/>), остаётся <b>215 реальных</b> → 430 NR-файлов на
/// полный прогон.
/// </summary>
public class WorldAquaticsCountriesProvider : IRecordCountriesProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Коды, которые источник отдаёт наравне со странами, но страной не являются (И-24,
    /// решение Влада 16.09.2026 — убрать). Список ЯВНЫЙ и другим быть не может: у этих
    /// записей регион заполнен («Europe» у ГДР), alpha-3 правильный, GUID настоящий — по
    /// форме они неотличимы от действующих стран, отличие только в том, что стоит за кодом.
    ///
    /// Две группы, обе убираем:
    /// 1. <b>Государства и объединённые команды, которых больше нет:</b> AHO (Нидерландские
    ///    Антилы), ANZ (Австралазия), CIS (СНГ), EUA (Объединённая германская команда),
    ///    EUN (Объединённая команда-92), FRG и GDR (ФРГ и ГДР), RHO (Родезия), SAA (Саар),
    ///    SCG (Сербия и Черногория), TCH (Чехословакия), UAR (ОАР), URS (СССР),
    ///    VNM (Южный Вьетнам), WIN (Вест-Индия), YUG (Югославия).
    /// 2. <b>Нейтральные сборные:</b> ART (сборная беженцев), NIA (нейтральные независимые
    ///    атлеты), ROC, RSF. Это тот же класс, что AIN/EOR/FINA из признака 1, — им просто
    ///    проставили регион.
    ///
    /// ⚠ Цена решения: рекорд, который источник держит ТОЛЬКО под старым кодом, исчезает
    /// вместе с ним. Живой случай — 400 к/п ж 50 м у Германии: строка Петры Шнайдер
    /// (04:36.10, 1982) помечена в источнике как <c>GDR</c>, и у действующей GER эта
    /// дисциплина остаётся пустой. Подробности — docs/data-integrity.md, И-24.
    /// </summary>
    private static readonly HashSet<string> NotCurrentCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "AHO", "ANZ", "CIS", "EUA", "EUN", "FRG", "GDR", "RHO", "SAA", "SCG",
        "TCH", "UAR", "URS", "VNM", "WIN", "YUG",
        "ART", "NIA", "ROC", "RSF",
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
    /// Отсеиваем по четырём признакам, каждый — не вкусовщина:
    /// 1. <c>RegionName</c> пуст → это не страна, а псевдо-сборная: AIN, CLB, EOR, FINA,
    ///    IOA, IOC, IOP, NAA, NAB, NAC, SMF (все 11 — с пустым регионом, других отличий нет).
    /// 2. код не из трёх букв → у всех реальных стран ровно alpha-3, четырёхбуквенный
    ///    <c>FINA</c> — как раз псевдо-сборная. Код уедет в <c>Record.RegionCode</c>, а там
    ///    длина — часть контракта.
    /// 3. <c>Id</c> не разбирается как GUID → в URL отчёта пойдёт только распознанный GUID,
    ///    строкой из ответа мы query не склеиваем.
    /// 4. код в <see cref="NotCurrentCountries"/> → государства, которых больше нет, и
    ///    нейтральные сборные. У них регион ЗАПОЛНЕН, поэтому признак 1 их не ловит.
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

            if (NotCurrentCountries.Contains(code)) continue;

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

        // Порядок прогона детерминированный — по нему же читается отчёт «страна N из 215».
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
