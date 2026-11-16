using Microsoft.Extensions.Configuration;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.WorldRecords;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Мировые + национальные рекорды (<c>world/open</c>, <c>country/&lt;страна&gt;/open</c>) с
/// api.worldaquatics.com. Чьи NR качать, задаёт RecordsImport:WorldAquaticsNationalCountryId
/// (внутренний GUID страны в API); по умолчанию — Израиль (домашний регион проекта).
///
/// <b>Автозагрузка — JSON-выдача <c>/fina/records/SW</c> (с 16.11.2026), а не XLSX-отчёт.</b>
/// Отчёт <c>/fina/records/report</c> собирается долго, CloudFront перед ним режет запрос 504-м и
/// КЭШИРУЕТ ошибку (<c>X-Cache: Error from cloudfront</c>): первый боевой прогон джоба
/// records-check на проде дважды подряд упал на WR SCM, а повторы получали тот же 504 из кэша за
/// 15 мс. JSON того же API отдавал WR и NR Израиля за ~7 с. Так же раньше ушёл на JSON
/// wa-junior (отчёт WJ — 504 через 90 с). Разбор общий — <see cref="WorldAquaticsRecordsJson"/>.
///
/// Чем JSON беднее XLSX: у эстафет нет состава, только команда. Такие строки помечены
/// <see cref="ParsedRecordDto.HolderIsTeam"/>, и дифф у них держателя не сравнивает — состав,
/// принесённый отчётом раньше, в базе остаётся, пока рекорд не побьют.
///
/// Ручная загрузка (Fetch с файлами в /Admin/Import) — по-прежнему XLSX-отчёты, тем же
/// парсером, что и раньше: на случай, когда JSON недоступен, а файлы скачаны руками.
///
/// SSRF: URL целиком собираются из константы домена + жёстко заданных query-параметров —
/// пользовательский ввод в URL никогда не попадает; whitelist домена и HTTP-клиент —
/// в <see cref="WorldAquaticsSource"/>, один на все источники worldaquatics.
/// </summary>
public class WorldRecordsSourceProvider : IRecordSourceProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WorldRecordsParser _parser;
    private readonly string _nationalCountryId;
    private readonly TimeSpan _retryDelay;

    /// <summary>Таймаут на запрос: JSON отвечает за ~7 с (замер 16.11.2026), с большим запасом.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Пауза перед повтором: шлюз источника отвечает 504 под нагрузкой, сразу же — бесполезно.</summary>
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>Пол записи в query выдачи: F, M и X — смешанные эстафеты.</summary>
    private static readonly string[] Genders = ["F", "M", "X"];

    private static readonly string[] Pools = ["LCM", "SCM"];

    /// <summary>Мировые: <c>world/open</c>, имя держателя — как в XLSX-парсере.</summary>
    private static readonly WaJsonAxes WorldAxes = OpenAxes("world", "");

    /// <summary>Национальные: <c>country/&lt;nationalityCode&gt;/open</c> — регион из строки, как NF Code в отчёте.</summary>
    private static readonly WaJsonAxes NationalAxes = OpenAxes("country", null);

    /// <param name="retryDelay">Пауза между повторами; в тестах — ноль, чтобы не спать.</param>
    public WorldRecordsSourceProvider(IHttpClientFactory httpClientFactory, WorldRecordsParser parser,
        IConfiguration? configuration = null, TimeSpan? retryDelay = null)
    {
        _httpClientFactory = httpClientFactory;
        _parser = parser;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
        var configured = configuration?["RecordsImport:WorldAquaticsNationalCountryId"];
        _nationalCountryId = string.IsNullOrWhiteSpace(configured) ? WorldAquaticsSource.IsraelCountryId : configured;
    }

    public string Source => "worldrecords";

    public Task<IReadOnlyList<ParsedRecordDto>> FetchAsync(RecordSourceRequest request, CancellationToken ct = default) =>
        request.PrimaryStream != null
            ? Task.FromResult(ParseXlsx(request))
            : FetchJsonAsync(ct);

    /// <summary>
    /// Автозагрузка: WR и NR × пол (F/M/X) × бассейн — 12 запросов. Каждый со своими повторами
    /// (<see cref="WorldAquaticsDownload"/>); один упавший после всех попыток роняет источник
    /// целиком — половина справочника хуже, чем «не проверено».
    /// </summary>
    private async Task<IReadOnlyList<ParsedRecordDto>> FetchJsonAsync(CancellationToken ct)
    {
        var client = WorldAquaticsSource.CreateClient(_httpClientFactory, RequestTimeout);

        var queries = new List<(string Query, WaJsonAxes Axes, string What)>();
        foreach (var gender in Genders)
            foreach (var pool in Pools)
                queries.Add(($"recordCode=WR&gender={gender}&pool={pool}", WorldAxes, $"WR {gender} {pool}"));
        foreach (var gender in Genders)
            foreach (var pool in Pools)
                queries.Add(($"recordCode=NR&gender={gender}&pool={pool}&countryId={_nationalCountryId}",
                    NationalAxes, $"NR {gender} {pool}"));

        // По одному: 12 запросов по ~7 с — около полутора минут; двенадцать параллельных к шлюзу,
        // который и так режет долгие запросы, — лишний риск 504.
        var parsed = new List<ParsedRecordDto>();
        foreach (var q in queries)
        {
            await using var body = await WorldAquaticsDownload.DownloadAsync(
                client, WorldAquaticsSource.RecordsJsonUrl(q.Query), q.What, _retryDelay, ct);
            parsed.AddRange(WorldAquaticsRecordsJson.Parse(body, q.Axes).Records);
        }

        return parsed;
    }

    private static WaJsonAxes OpenAxes(string regionType, string? regionCode) =>
        WorldAquaticsRecordsJson.OpenAxes(regionType, regionCode, "worldrecords");

    /// <summary>Ручная загрузка XLSX-отчётов (WR SCM + WR LCM) — прежний путь.</summary>
    private IReadOnlyList<ParsedRecordDto> ParseXlsx(RecordSourceRequest request)
    {
        var secondary = request.SecondaryStream
            ?? throw new InvalidOperationException("Нужны минимум 2 файла (WR SCM + WR LCM).");

        var parseRequest = new ParseRequest(
            request.PrimaryStream!, "WR_SCM.xlsx",
            secondary, "WR_LCM.xlsx",
            IsAward: false,
            PoolType: null,
            ExtraStreams: null);

        var parsed = new List<ParsedRecordDto>();
        foreach (var r in _parser.Parse(parseRequest))
        {
            var distance = r.EventStyleLen.EndsWith('m') ? r.EventStyleLen : r.EventStyleLen + "m";
            if (WorldAquaticsSource.RecordGender(r.EventStyleGender, distance) is not { } gender)
                continue;

            var isWorld = string.Equals(r.Note, "WR", StringComparison.OrdinalIgnoreCase);
            var regionType = isWorld ? "world" : "country";
            var regionCode = isWorld ? "" : (r.Note ?? "").Trim().ToUpperInvariant();

            parsed.Add(new ParsedRecordDto(
                RegionType: regionType,
                RegionCode: regionCode,
                Category: "open",
                AgeKey: "",
                Gender: gender,
                PoolType: r.PoolType,
                Style: r.EventStyleName,
                Distance: distance,
                Time: r.Time,
                HolderName: $"{r.FirstName} {r.LastName}".Trim(),
                Club: null,
                HolderCountry: r.Country,
                RecordDate: r.Date));
        }

        return parsed;
    }
}
