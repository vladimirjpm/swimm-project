using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Национальные рекорды одной страны с api.worldaquatics.com (этап 11.1.3 п. 2) по GUID страны
/// из <see cref="RecordCountryDto"/> — JSON-выдача <c>/fina/records/SW</c>, шесть запросов на
/// страну (пол F/M/X × бассейн LCM/SCM).
///
/// ⚠ <b>С 16.11.2026 — JSON, а не XLSX-отчёт.</b> Отчёт <c>/fina/records/report</c> источник
/// стал ограничивать по частоте: после пары десятков стран CloudFront отвечает 429 с
/// <c>Retry-After: 300</c>, и боевой прогон дважды за вечер потерял половину стран (И-67).
/// JSON того же API в это же время отвечал 200 за 4–6 с. Разбор — общий с
/// <c>worldrecords</c> (<see cref="WorldAquaticsRecordsJson"/>), поэтому строки выходят в той
/// же форме, что из отчёта (сверено вживую с прогоном 22.09). Эстафеты: состава JSON не отдаёт,
/// держатель — команда (<see cref="ParsedRecordDto.HolderIsTeam"/>), дифф держателя не сравнивает
/// и состав в базе сохраняется.
///
/// Что держится по-прежнему:
/// <list type="bullet">
/// <item><b><c>RegionCode</c> берётся из запроса.</b> Строка, где источник назвал другую
/// федерацию (или никакую — пустой <c>nationalityCode</c>), не пишется вовсе, а уходит в отчёт
/// прогона (решение Влада 16.09.2026, план §7 п. 2).</item>
/// <item><b>Повторы на каждый запрос</b> — общий <see cref="WorldAquaticsDownload"/>: сеть,
/// таймаут, 5xx, а 429 — с ожиданием по <c>Retry-After</c>.</item>
/// <item><b>Мировые рекорды — отдельным методом.</b> <see cref="FetchWorldAsync"/> качает WR один
/// раз на прогон, а не на страну (11.1.2).</item>
/// </list>
/// </summary>
public class WorldAquaticsCountryFetcher : IRecordCountryFetcher
{
    /// <summary>Таймаут на запрос — с запасом: JSON отдаётся за секунды, но шлюз бывает медленным.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Попыток на запрос: первая + два повтора.</summary>
    public const int MaxAttempts = WorldAquaticsDownload.MaxAttempts;

    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>Пол записи в query выдачи: F, M и X — смешанные эстафеты.</summary>
    private static readonly string[] Genders = ["F", "M", "X"];

    private static readonly string[] Pools = ["LCM", "SCM"];

    private static readonly WaJsonAxes WorldAxes =
        WorldAquaticsRecordsJson.OpenAxes("world", "", "worldrecords-countries");

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeSpan _retryDelay;
    private readonly WorldAquaticsDownload.Delay? _delay;

    /// <param name="retryDelay">Пауза между повторами; в тестах — ноль, чтобы не спать.</param>
    /// <param name="delay">Чем ждать (повтор, Retry-After); в тестах — запись вместо сна.</param>
    public WorldAquaticsCountryFetcher(
        IHttpClientFactory httpClientFactory, TimeSpan? retryDelay = null, WorldAquaticsDownload.Delay? delay = null)
    {
        _httpClientFactory = httpClientFactory;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
        _delay = delay;
    }

    public string Source => "worldrecords";

    public async Task<RecordCountryFetchResult> FetchAsync(
        RecordCountryDto country, CancellationToken ct = default)
    {
        // Страна приходит из списка источника, где GUID уже проверен, — но в query уходит
        // именно эта строка, поэтому проверяем ещё раз здесь: подстановка в URL не должна
        // зависеть от того, кто собрал DTO.
        if (!Guid.TryParse(country.SourceId, out var countryId))
            throw new InvalidOperationException(
                $"Идентификатор страны {country.SourceId} ({country.Code}) не похож на GUID источника.");

        var code = country.Code.Trim().ToUpperInvariant();
        var axes = WorldAquaticsRecordsJson.OpenAxes("country", code, $"NR {code}");

        var rows = await FetchAllAsync(
            $"recordCode=NR&countryId={countryId}", axes, $"NR для {code}", ct);

        return Split(code, rows);
    }

    /// <summary>
    /// Мировые рекорды — шесть запросов на весь прогон (11.1.2). Национальные рекорды из WR-выдачи
    /// здесь НЕ берём: ровно так в базе и заводились одиночки <c>country/USA/open</c> и
    /// <c>country/JAM/open</c> (data-integrity И-19). В прогоне национальный рекорд приходит только
    /// из NR-выдачи своей страны.
    /// </summary>
    public Task<IReadOnlyList<ParsedRecordDto>> FetchWorldAsync(CancellationToken ct = default) =>
        FetchAllAsync("recordCode=WR", WorldAxes, "WR", ct);

    /// <summary>Пол × бассейн по одному: шесть параллельных запросов — лишний повод для лимита.</summary>
    private async Task<IReadOnlyList<ParsedRecordDto>> FetchAllAsync(
        string query, WaJsonAxes axes, string what, CancellationToken ct)
    {
        var client = WorldAquaticsSource.CreateClient(_httpClientFactory, RequestTimeout);
        var rows = new List<ParsedRecordDto>();

        foreach (var gender in Genders)
            foreach (var pool in Pools)
            {
                await using var body = await WorldAquaticsDownload.DownloadAsync(
                    client, WorldAquaticsSource.RecordsJsonUrl($"{query}&gender={gender}&pool={pool}"),
                    $"{what} {gender} {pool}", _retryDelay, ct, _delay);
                rows.AddRange(WorldAquaticsRecordsJson.Parse(body, axes).Records);
            }

        return rows;
    }

    /// <summary>
    /// Строки выдачи страны → рекорды и непринятые. Категория всегда <c>open</c>: age и masters
    /// остаются исключительно израильскими (решение Влада 29.07.2026). Федерация строки —
    /// <c>nationalityCode</c> (в отчёте это был NF Code); чужая или пустая — в отчёт прогона.
    /// </summary>
    private static RecordCountryFetchResult Split(string requestedCode, IReadOnlyList<ParsedRecordDto> rows)
    {
        var records = new List<ParsedRecordDto>();
        var mismatches = new List<RecordCountryMismatchDto>();

        foreach (var r in rows)
        {
            var reported = (r.HolderCountry ?? "").Trim();
            if (!string.Equals(reported, requestedCode, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add(new RecordCountryMismatchDto(
                    RequestedCode: requestedCode,
                    ReportedCountry: reported,
                    PoolType: r.PoolType,
                    Style: r.Style,
                    Distance: r.Distance,
                    Gender: r.Gender,
                    Time: r.Time));
                continue;
            }

            records.Add(r with { HolderCountry = requestedCode });
        }

        return new RecordCountryFetchResult(requestedCode, records, mismatches);
    }
}
