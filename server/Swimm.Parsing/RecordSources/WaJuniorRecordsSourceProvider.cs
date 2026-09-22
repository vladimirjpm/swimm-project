using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Мировые ЮНИОРСКИЕ рекорды (<c>world/junior</c>, World Junior Records) из JSON-эндпоинта
/// World Aquatics — того самого, которым питается страница
/// <c>worldaquatics.com/swimming/records</c> (фильтр Record Type → World Junior Records).
///
/// Почему не XLSX-отчёт, как у <see cref="WorldRecordsSourceProvider"/>: отчёт на
/// <c>recordCode=WJ</c> отвечает 504 через 90 с (J0, 21.09.2026, повторено для SCM и LCM) —
/// собрать его шлюз не успевает. JSON тот же набор отдаёт, но тоже медленно: 30–90 с.
///
/// Полоса WJR — правило World Aquatics, возраст на 31 декабря: женщины 14–17, мужчины 15–18.
/// Она пишется прямо в <c>AgeKey</c> («14-17» / «15-18», решение Влада 21.09.2026), чтобы матч
/// «возраст в полосе» читал её из данных, а не из константы.
///
/// Несколько строк на дисциплину — норма ответа (повторённый рекорд; побитый рекорд остаётся
/// в выдаче, хотя <c>current=true</c>). Провайдер отдаёт их все: лучшее время выбирает
/// <c>RecordDiffService.DeduplicateByAxes</c>, как и для WR.
///
/// Эстафеты (Э1 плана docs/plans/records-relays-plan.md, 22.09.2026): однополые пишутся в той
/// же форме, что у WR — <c>Distance=4X100m</c>, стиль <c>freestyle</c> / <c>individual_medley</c>.
/// Имён участников источник для эстафет не отдаёт, держатель — команда (<c>fullName</c>).
/// Смешанные (<c>gender=X</c>, <c>disciplineGender=2</c>) пишутся с полом <c>mixed</c> (Э3) и
/// полосой <see cref="MixedBand"/> — объединение женской и мужской: в четвёрке есть обе.
/// Эстафетная строка, не легшая в оси, считается и уходит в лог — не молча.
/// </summary>
public class WaJuniorRecordsSourceProvider : IRecordSourceProvider
{
    public const string SourceKey = "wa-junior";

    public const string FemaleBand = "14-17";
    public const string MaleBand = "15-18";
    /// <summary>Полоса смешанной эстафеты: девушки 14–17 + юноши 15–18 (решение 22.09.2026, Э3).</summary>
    public const string MixedBand = "14-18";

    /// <summary>Пол × бассейн — по запросу на пару; X — смешанные эстафеты (Э3).</summary>
    private static readonly (string Gender, string Pool)[] Queries =
        [("F", "LCM"), ("F", "SCM"), ("M", "LCM"), ("M", "SCM"), ("X", "LCM"), ("X", "SCM")];

    /// <summary>Индивидуальные стили; «Medley» без «Relay» — всегда комплекс.</summary>
    private static readonly Dictionary<string, string> Styles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Freestyle"] = "freestyle",
        ["Backstroke"] = "backstroke",
        ["Breaststroke"] = "breaststroke",
        ["Butterfly"] = "butterfly",
        ["Medley"] = "individual_medley",
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WaJuniorRecordsSourceProvider> _logger;

    public WaJuniorRecordsSourceProvider(
        IHttpClientFactory httpClientFactory, ILogger<WaJuniorRecordsSourceProvider>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<WaJuniorRecordsSourceProvider>.Instance;
    }

    public string Source => SourceKey;

    public async Task<IReadOnlyList<ParsedRecordDto>> FetchAsync(
        RecordSourceRequest request, CancellationToken ct = default)
    {
        var parsed = new List<ParsedRecordDto>();
        var relays = 0;

        void Take(Stream json)
        {
            var result = Parse(json);
            parsed.AddRange(result.Records);
            relays += result.SkippedRelays;
        }

        if (request.PrimaryStream != null)
        {
            // Ручная загрузка: файл описывает себя сам (пол и бассейн — в каждой строке),
            // поэтому какой из двух слотов какой, не важно.
            Take(request.PrimaryStream);
            if (request.SecondaryStream != null) Take(request.SecondaryStream);
        }
        else
        {
            // 30–90 с на запрос (J0) — таймаут вдвое; все запросы параллельно, иначе
            // Fetch в админке идёт до шести минут.
            var client = WorldAquaticsSource.CreateClient(_httpClientFactory, TimeSpan.FromSeconds(180));
            var bodies = await Task.WhenAll(Queries.Select(q => FetchJsonAsync(client, q.Gender, q.Pool, ct)));
            foreach (var body in bodies)
            {
                await using (body) Take(body);
            }
        }

        if (relays > 0)
            _logger.LogWarning(
                "wa-junior: пропущено {Count} эстафетных строк, не легших в оси Record — см. docs/plans/records-relays-plan.md", relays);

        return parsed;
    }

    public sealed record ParseResult(IReadOnlyList<ParsedRecordDto> Records, int SkippedRelays);

    /// <summary>
    /// Один ответ эндпоинта → строки справочника. Проверяет, что ответ полный: страница одна
    /// (<c>pageSize=50</c>, строк 18–25), и если источник однажды начнёт резать выдачу, мы
    /// должны упасть, а не записать половину справочника.
    /// </summary>
    public static ParseResult Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("wa-junior: в ответе нет массива records — формат источника поменялся?");

        if (root.TryGetProperty("totalRowCount", out var total) && total.TryGetInt32(out var totalRows)
            && totalRows != records.GetArrayLength())
            throw new InvalidOperationException(
                $"wa-junior: источник отдал {records.GetArrayLength()} строк из {totalRows} — выдача " +
                "стала постраничной, нужен проход по страницам.");

        var result = new List<ParsedRecordDto>();
        var relays = 0;
        foreach (var row in records.EnumerateArray())
        {
            var group = Str(row, "disciplineGroup");
            var isRelay = group.Contains("Relay", StringComparison.OrdinalIgnoreCase);

            var dto = ToRecord(row, group, isRelay);
            if (dto != null) result.Add(dto);
            else if (isRelay) relays++;
        }

        return new ParseResult(result, relays);
    }

    /// <summary>
    /// null — строка не ложится в оси <c>Record</c> (незнакомый стиль, пол, бассейн): как у
    /// остальных источников, расхождение числа строк видно в диффе.
    /// </summary>
    private static ParsedRecordDto? ToRecord(JsonElement row, string group, bool isRelay)
    {
        // «Freestyle Relay» / «Medley Relay» → стиль без слова Relay; эстафету несёт дистанция.
        var styleKey = isRelay ? group.Replace("Relay", "", StringComparison.OrdinalIgnoreCase).Trim() : group;
        if (!Styles.TryGetValue(styleKey, out var style)) return null;
        if (isRelay && style is not ("freestyle" or "individual_medley")) return null;

        // Коды источника (J0, Э3): disciplineGender 0 — мужчины, 1 — женщины, 2 — смешанные;
        // pool 0 — LCM, 1 — SCM.
        var (gender, band) = Int(row, "disciplineGender") switch
        {
            0 => ("male", MaleBand),
            1 => ("female", FemaleBand),
            2 when isRelay => ("mixed", MixedBand),
            _ => (null, null),
        };
        if (gender is null) return null;

        var pool = Int(row, "pool") switch
        {
            0 => "50m",
            1 => "25m",
            _ => null,
        };
        if (pool is null) return null;

        if (Int(row, "disciplineDistance") is not int distance || distance <= 0) return null;

        // timeFormatted — ровно та форма, в которой лежит world/open («46.40», «01:42.00»);
        // сырое time («00:00:24») без долей не годится.
        var time = Str(row, "timeFormatted");
        if (time.Length == 0) return null;

        var holder = isRelay
            ? Str(row, "fullName")
            : HolderName(Str(row, "preferredFirstName"), Str(row, "preferredLastName"));
        var country = Str(row, "nationalityCode");

        return new ParsedRecordDto(
            RegionType: "world",
            RegionCode: "",
            Category: "junior",
            AgeKey: band!,
            Gender: gender,
            PoolType: pool,
            Style: style,
            Distance: (isRelay ? "4X" : "") + distance + "m",
            Time: time,
            HolderName: holder.Length == 0 ? null : holder,
            Club: null,
            HolderCountry: country.Length == 0 ? null : country,
            RecordDate: NormalizeDate(Str(row, "date")));
    }

    /// <summary>
    /// «Claire» + «CURZAN» → «Claire Curzan» — как в world/open («Имя Фамилия», фамилия не
    /// капсом): держатели обеих осей стоят рядом в одной карточке.
    /// </summary>
    public static string HolderName(string first, string last)
    {
        var lastTitle = string.Join(' ', last
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(TitleCaseWord));
        return $"{first.Trim()} {lastTitle}".Trim();
    }

    /// <summary>Слово капсом → с заглавной; дефисные части — каждая («SMITH-JONES» → «Smith-Jones»).</summary>
    private static string TitleCaseWord(string word)
    {
        if (word != word.ToUpperInvariant()) return word; // уже в смешанном регистре — не трогаем
        return string.Join('-', word.Split('-').Select(p =>
            p.Length <= 1 ? p : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    /// <summary>«2021-05-14T00:00:00» → «14/05/2021» — форма дат world/open.</summary>
    private static string? NormalizeDate(string source)
    {
        if (source.Length == 0) return null;
        return DateTime.TryParse(source, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : source.Trim();
    }

    private static string Str(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()?.Trim() ?? ""
            : "";

    private static int? Int(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;

    private static async Task<Stream> FetchJsonAsync(
        HttpClient client, string gender, string pool, CancellationToken ct)
    {
        var uri = WorldAquaticsSource.RecordsJsonUrl($"recordCode=WJ&gender={gender}&pool={pool}");

        // Одна повторная попытка на 5xx: шлюз источника режет долгие запросы 504-м (J0), а
        // второй заход часто попадает в уже прогретый кэш у них.
        var response = await client.GetAsync(uri, ct);
        if ((int)response.StatusCode >= 500)
        {
            response.Dispose();
            response = await client.GetAsync(uri, ct);
        }
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"wa-junior: {gender} {pool} — источник ответил {(int)response.StatusCode}. " +
                "Эндпоинт медленный (30–90 с); 504 — повторить позже.");

        var ms = new MemoryStream();
        await response.Content.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }
}
