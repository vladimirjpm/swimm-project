using System.Net;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Models;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.WorldRecords;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Национальные рекорды одной страны с api.worldaquatics.com (этап 11.1.3 п. 2) — два
/// XLSX-отчёта (NR SCM + NR LCM) по GUID страны из <see cref="RecordCountryDto"/>.
///
/// Чем отличается от <see cref="WorldRecordsSourceProvider"/>, кроме страны в запросе:
/// <list type="bullet">
/// <item><b><c>RegionCode</c> берётся из запроса.</b> Провайдер выводит его из строки отчёта
/// (<c>NF Code</c>, а если тот пуст — из колонки <c>Country</c>, то есть из страны МЕСТА
/// соревнования). На 215 странах этот фоллбек дал бы регионы вида «Great Britain», поэтому
/// здесь он запрещён: строка, где источник назвал другую федерацию, не пишется вовсе, а
/// уходит в отчёт прогона (решение Влада 16.09.2026, план §7 п. 2).</item>
/// <item><b>Таймаут 120 с и два повтора.</b> Замер 15.09: отчёты отдаются от 2 до 53 с — то
/// есть прежних 30 с не хватало двум файлам из восьми. Прогон идёт час-два, и одна
/// подвисшая страна не повод терять весь батч.</item>
/// <item><b>Мировые рекорды — отдельным методом.</b> <see cref="FetchWorldAsync"/> качает
/// WR SCM/LCM один раз на прогон, а не на страну (11.1.2).</item>
/// </list>
/// </summary>
public class WorldAquaticsCountryFetcher : IRecordCountryFetcher
{
    /// <summary>Таймаут на файл. 120 с — с запасом к замеренным 53 с (план §3а).</summary>
    private static readonly TimeSpan FileTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Попыток на файл: первая + два повтора.</summary>
    public const int MaxAttempts = 3;

    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WorldRecordsParser _parser;
    private readonly TimeSpan _retryDelay;

    /// <param name="retryDelay">Пауза между повторами; в тестах — ноль, чтобы не спать.</param>
    public WorldAquaticsCountryFetcher(
        IHttpClientFactory httpClientFactory, WorldRecordsParser parser, TimeSpan? retryDelay = null)
    {
        _httpClientFactory = httpClientFactory;
        _parser = parser;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
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

        var client = WorldAquaticsSource.CreateClient(_httpClientFactory, FileTimeout);

        MemoryStream? scm = null, lcm = null;
        try
        {
            scm = await DownloadAsync(
                client, WorldAquaticsSource.ReportUrl($"recordCode=NR&pool=SCM&countryId={countryId}"),
                code, "NR SCM", ct);
            lcm = await DownloadAsync(
                client, WorldAquaticsSource.ReportUrl($"recordCode=NR&pool=LCM&countryId={countryId}"),
                code, "NR LCM", ct);

            // Бассейн парсер берёт из колонки Pool каждой строки, а имена файлов идут только
            // в отладочный лог — поэтому пишем в них страну: в логе прогона по 235 странам
            // это единственный способ понять, чей файл разбирается.
            var rows = _parser.Parse(new ParseRequest(
                scm, $"NR_SCM_{code}.xlsx",
                lcm, $"NR_LCM_{code}.xlsx",
                IsAward: false,
                PoolType: null));

            return MapRows(code, rows);
        }
        finally
        {
            if (scm != null) await scm.DisposeAsync();
            if (lcm != null) await lcm.DisposeAsync();
        }
    }

    /// <summary>
    /// Мировые рекорды — два файла на весь прогон (11.1.2). Национальные рекорды из
    /// WR-отчёта здесь НЕ берём: ровно так в базе и заводились одиночки
    /// <c>country/USA/open</c> и <c>country/JAM/open</c> (data-integrity И-19). В прогоне
    /// национальный рекорд приходит только из NR-отчёта своей страны.
    /// </summary>
    public async Task<IReadOnlyList<ParsedRecordDto>> FetchWorldAsync(CancellationToken ct = default)
    {
        var client = WorldAquaticsSource.CreateClient(_httpClientFactory, FileTimeout);

        MemoryStream? scm = null, lcm = null;
        try
        {
            scm = await DownloadAsync(
                client, WorldAquaticsSource.ReportUrl("pool=SCM&recordCode=WR"), "world", "WR SCM", ct);
            lcm = await DownloadAsync(
                client, WorldAquaticsSource.ReportUrl("pool=LCM&recordCode=WR"), "world", "WR LCM", ct);

            var rows = _parser.Parse(new ParseRequest(
                scm, "WR_SCM.xlsx",
                lcm, "WR_LCM.xlsx",
                IsAward: false,
                PoolType: null));

            var world = new List<ParsedRecordDto>();
            foreach (var r in rows)
            {
                var distance = r.EventStyleLen.EndsWith('m') ? r.EventStyleLen : r.EventStyleLen + "m";
                if (WorldAquaticsSource.RecordGender(r.EventStyleGender, distance) is not { } gender)
                    continue;

                // Тип рекорда «WR» парсер ставит и повторённому «=WR» (И-19). Всё, что им не
                // помечено, в мировые не попадает — и в страны из этого файла тоже.
                if (!string.Equals(r.Note, "WR", StringComparison.OrdinalIgnoreCase))
                    continue;

                world.Add(new ParsedRecordDto(
                    RegionType: "world",
                    RegionCode: "",
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

            return world;
        }
        finally
        {
            if (scm != null) await scm.DisposeAsync();
            if (lcm != null) await lcm.DisposeAsync();
        }
    }

    /// <summary>
    /// Строки отчёта → рекорды страны. Категория всегда <c>open</c>: age и masters остаются
    /// исключительно израильскими (решение Влада 29.07.2026) — у других стран их у источника
    /// просто нет.
    /// </summary>
    private static RecordCountryFetchResult MapRows(string requestedCode, IEnumerable<Result> rows)
    {
        var records = new List<ParsedRecordDto>();
        var mismatches = new List<RecordCountryMismatchDto>();

        foreach (var r in rows)
        {
            // Смешанные эстафеты идут с полом mixed (Э3); прочее вне осей Record — мимо.
            var distance = r.EventStyleLen.EndsWith('m') ? r.EventStyleLen : r.EventStyleLen + "m";
            if (WorldAquaticsSource.RecordGender(r.EventStyleGender, distance) is not { } gender)
                continue;

            // r.Country — это NF Code строки (а если он пуст, парсер подставил туда страну
            // места соревнования). Тип рекорда (r.Note) здесь не спрашиваем сознательно: у
            // строки «NR, WR» он схлопывается в WR, и национальный рекорд страны потерялся бы.
            var reported = (r.Country ?? "").Trim();

            if (!string.Equals(reported, requestedCode, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add(new RecordCountryMismatchDto(
                    RequestedCode: requestedCode,
                    ReportedCountry: reported,
                    PoolType: r.PoolType,
                    Style: r.EventStyleName,
                    Distance: distance,
                    Gender: gender,
                    Time: r.Time));
                continue;
            }

            records.Add(new ParsedRecordDto(
                RegionType: "country",
                RegionCode: requestedCode,
                Category: "open",
                AgeKey: "",
                Gender: gender,
                PoolType: r.PoolType,
                Style: r.EventStyleName,
                Distance: distance,
                Time: r.Time,
                HolderName: $"{r.FirstName} {r.LastName}".Trim(),
                Club: null,
                HolderCountry: requestedCode,
                RecordDate: r.Date));
        }

        return new RecordCountryFetchResult(requestedCode, records, mismatches);
    }

    /// <summary>
    /// Скачивание одного отчёта с повторами. Повторяем только то, что может пройти само:
    /// обрыв сети, таймаут, 5xx и 429. На 4xx повтор бессмысленен — кривой GUID лучше
    /// увидеть сразу, чем через три попытки по две минуты.
    /// </summary>
    private async Task<MemoryStream> DownloadAsync(
        HttpClient client, Uri url, string code, string what, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var last = attempt >= MaxAttempts;
            try
            {
                var response = await client.GetAsync(url, ct);

                if (IsTransient(response.StatusCode))
                {
                    if (last)
                        throw new InvalidOperationException(
                            $"{what} для {code}: источник отвечает {(int)response.StatusCode} " +
                            $"после {MaxAttempts} попыток.");

                    await Task.Delay(_retryDelay, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"{what} для {code}: источник ответил {(int)response.StatusCode} {response.ReasonPhrase}.");

                var ms = new MemoryStream();
                await response.Content.CopyToAsync(ms, ct);
                ms.Position = 0;
                return ms;
            }
            catch (Exception ex) when (!last && IsRetriable(ex, ct))
            {
                await Task.Delay(_retryDelay, ct);
            }
            catch (Exception ex) when (last && IsRetriable(ex, ct))
            {
                throw new InvalidOperationException(
                    $"{what} для {code} не скачался за {MaxAttempts} попытки: {ex.Message}", ex);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        (int)status >= 500 || status == HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Таймаут клиента приходит как <see cref="TaskCanceledException"/> — тем же типом, что и
    /// отмена прогона админом. Различаем по токену: отмену повторять нельзя.
    /// </summary>
    private static bool IsRetriable(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);
}
