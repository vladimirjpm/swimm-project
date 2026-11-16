using System.Net;
using System.Net.Http;
using System.Text;
using Swimm.Application.Dtos;
using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Национальные рекорды одной страны по GUID (этап 11.1.3 п. 2) — с 16.11.2026 из JSON-выдачи
/// <c>/fina/records/SW</c> (XLSX-отчёт источник ограничил по частоте, И-67). Ответы источника
/// подделываются в памяти в форме живых — сеть в тестах запрещена.
/// </summary>
public class WorldAquaticsCountryFetcherTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly RecordCountryDto Anguilla =
        new("AGU", "27624a51-d5fb-4800-a49a-8dd1faf7b6af", "Anguilla", "Americas");

    private static string Row(int pool, int gender, string group, int distance, string time,
        string nationality, string fullName, bool relay = false) => $$"""
        {"pool":{{pool}},"disciplineGender":{{gender}},"timeFormatted":"{{time}}","nationalityCode":"{{nationality}}",
         "fullName":"{{fullName}}","disciplineDistance":{{distance}},"disciplineGroup":"{{group}}",
         "date":"2025-07-20T00:00:00","isRelay":{{(relay ? "true" : "null")}}}
        """;

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(params string[] rows) => _ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"records":[{{string.Join(",", rows)}}],"totalRowCount":{{rows.Length}}}""",
                Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// Ответы по query: пол и бассейн. Остальные из шести запросов — пустые, как у живой
    /// Ангильи (женщин 25 м и смешанных эстафет у неё нет).
    /// </summary>
    private static StubHandler Country() => new(request =>
    {
        var q = request.RequestUri!.Query;
        if (q.Contains("gender=F") && q.Contains("pool=SCM"))
            return Json(
                Row(1, 1, "Freestyle", 50, "24.11", "AGU", "SMITH Jane"),
                // Пустая федерация — в отчёт, не в базу (план §3а).
                Row(1, 1, "Backstroke", 100, "01:02.02", "", "BROWN Beth"),
                // Чужая федерация в выдаче страны — тоже в отчёт.
                Row(1, 1, "Butterfly", 200, "02:10.44", "USA", "DOE Ann"))(request);
        if (q.Contains("gender=X") && q.Contains("pool=SCM"))
            return Json(Row(1, 2, "Freestyle Relay", 100, "03:30.01", "AGU", "Anguilla", relay: true))(request);
        if (q.Contains("gender=M") && q.Contains("pool=LCM"))
            return Json(Row(0, 0, "Breaststroke", 200, "02:05.48", "AGU", "ROE Rob"))(request);
        return Json()(request);
    });

    [Fact]
    public async Task FetchAsync_TakesRegionCodeFromRequest()
    {
        var result = await Fetcher(Country()).FetchAsync(Anguilla);

        Assert.Equal("AGU", result.Code);
        Assert.Equal(3, result.Records.Count);
        Assert.All(result.Records, r =>
        {
            Assert.Equal(("country", "AGU", "AGU"), (r.RegionType, r.RegionCode, r.HolderCountry));
            // age и masters остаются исключительно израильскими (решение Влада 29.07.2026).
            Assert.Equal(("open", ""), (r.Category, r.AgeKey));
        });

        var free = Assert.Single(result.Records, r => r is { Style: "freestyle", Gender: "female" });
        Assert.Equal(("25m", "50m", "24.11", "Jane Smith"), (free.PoolType, free.Distance, free.Time, free.HolderName));

        var breast = Assert.Single(result.Records, r => r.Style == "breaststroke");
        Assert.Equal(("50m", "200m", "20/07/2025"), (breast.PoolType, breast.Distance, breast.RecordDate));

        // Смешанная эстафета — рекорд страны с полом mixed; держатель — команда (состава нет).
        var mixed = Assert.Single(result.Records, r => r.Gender == "mixed");
        Assert.Equal(("25m", "freestyle", "4X100m", "03:30.01"), (mixed.PoolType, mixed.Style, mixed.Distance, mixed.Time));
        Assert.True(mixed.HolderIsTeam);
    }

    [Fact]
    public async Task FetchAsync_ReportsForeignAndEmptyFederation()
    {
        var result = await Fetcher(Country()).FetchAsync(Anguilla);

        Assert.Equal(2, result.Mismatches.Count);
        Assert.All(result.Mismatches, m => Assert.Equal("AGU", m.RequestedCode));

        var empty = Assert.Single(result.Mismatches, m => m.Style == "backstroke");
        Assert.Equal(("", "100m", "female", "01:02.02", "25m"),
            (empty.ReportedCountry, empty.Distance, empty.Gender, empty.Time, empty.PoolType));

        Assert.Equal("USA", Assert.Single(result.Mismatches, m => m.Style == "butterfly").ReportedCountry);

        Assert.DoesNotContain(result.Records, r => r.Style is "backstroke" or "butterfly");
    }

    [Fact]
    public async Task FetchAsync_AsksSixJsonQueriesOfOneCountry()
    {
        var handler = Country();

        await Fetcher(handler).FetchAsync(Anguilla);

        Assert.Equal(6, handler.Requests.Count);
        Assert.All(handler.Requests, url =>
        {
            Assert.StartsWith($"https://{WorldAquaticsSource.ApiHost}/fina/records/SW?", url);
            Assert.Contains("recordCode=NR", url);
            Assert.Contains($"countryId={Anguilla.SourceId}", url);
        });
        foreach (var g in new[] { "F", "M", "X" })
            foreach (var p in new[] { "LCM", "SCM" })
                Assert.Single(handler.Requests, u => u.Contains($"gender={g}") && u.Contains($"pool={p}"));

        // Мировые рекорды — один раз на прогон, а не на страну (11.1.2).
        Assert.DoesNotContain(handler.Requests, u => u.Contains("recordCode=WR"));
        // Отчёт XLSX больше не трогаем: на нём лимит частоты (И-67).
        Assert.DoesNotContain(handler.Requests, u => u.Contains("/records/report"));
    }

    [Fact]
    public async Task FetchWorldAsync_TakesWorldRecordsFromJson()
    {
        var handler = new StubHandler(request => request.RequestUri!.Query.Contains("gender=F&pool=SCM")
            ? Json(Row(1, 1, "Freestyle", 50, "22.75", "USA", "DOUGLASS Kate"))(request)
            : Json()(request));

        var world = await Fetcher(handler).FetchWorldAsync();

        var r = Assert.Single(world);
        Assert.Equal(("world", "", "open", "Kate Douglass", "USA"), (r.RegionType, r.RegionCode, r.Category, r.HolderName, r.HolderCountry));
        Assert.Equal(6, handler.Requests.Count);
        Assert.All(handler.Requests, u => Assert.Contains("recordCode=WR", u));
    }

    /// <summary>Прогон идёт час-два: 503 на одном запросе не повод терять страну.</summary>
    [Fact]
    public async Task FetchAsync_RetriesServerErrors()
    {
        var calls = 0;
        var handler = new StubHandler(request =>
            ++calls <= 2 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Country().Respond(request));

        var result = await Fetcher(handler).FetchAsync(Anguilla);

        Assert.Equal(8, handler.Requests.Count);   // два повтора первого запроса + шесть успешных
        Assert.Equal(3, result.Records.Count);
    }

    [Fact]
    public async Task FetchAsync_StopsAfterThreeAttempts()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetcher(handler).FetchAsync(Anguilla));

        Assert.Equal(WorldAquaticsCountryFetcher.MaxAttempts, handler.Requests.Count);
        Assert.Contains("AGU", ex.Message);
    }

    /// <summary>
    /// 429 с Retry-After: ждём, сколько просит источник, а не 2 с (16.11.2026 так терялась
    /// половина стран, И-67). Ожидание подменено — тест не спит пять минут.
    /// </summary>
    [Fact]
    public async Task FetchAsync_WaitsRetryAfterOnTooManyRequests()
    {
        var calls = 0;
        var handler = new StubHandler(request =>
        {
            if (++calls > 1) return Country().Respond(request);
            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(300));
            return limited;
        });
        var waits = new List<TimeSpan>();

        var result = await new WorldAquaticsCountryFetcher(new StubFactory(handler), TimeSpan.FromSeconds(2),
            (wait, _) => { waits.Add(wait); return Task.CompletedTask; }).FetchAsync(Anguilla);

        Assert.Equal([TimeSpan.FromSeconds(300)], waits);
        Assert.Equal(3, result.Records.Count);
    }

    [Theory]
    [InlineData(300, 300)]     // просьба источника — как есть
    [InlineData(1, 2)]         // меньше своей паузы — своя пауза
    [InlineData(3600, 360)]    // больше потолка — потолок
    public void RetryAfter_is_honoured_within_bounds(int askedSeconds, int expectedSeconds)
    {
        var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(askedSeconds));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds),
            WorldAquaticsDownload.WaitBeforeRetry(limited, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void RetryAfter_is_ignored_for_server_errors()
    {
        var gateway = new HttpResponseMessage(HttpStatusCode.GatewayTimeout);
        gateway.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(300));

        Assert.Equal(TimeSpan.FromSeconds(2), WorldAquaticsDownload.WaitBeforeRetry(gateway, TimeSpan.FromSeconds(2)));
    }

    /// <summary>Кривой GUID источник не «починит» — повторять незачем.</summary>
    [Fact]
    public async Task FetchAsync_DoesNotRetryClientErrors()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Fetcher(handler).FetchAsync(Anguilla));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_RejectsCountryIdThatIsNotGuid()
    {
        var handler = Country();
        var broken = Anguilla with { SourceId = "../../etc" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetcher(handler).FetchAsync(broken));

        Assert.Contains("GUID", ex.Message);
        Assert.Empty(handler.Requests);   // в сеть с таким идентификатором не ходим вовсе
    }

    /// <summary>
    /// Живая сверка JSON против архива прогона 22.09 (XLSX): строки страны должны совпасть по
    /// времени, дате и держателю (у эстафет держатель — команда, его не сравниваем). Расхождения
    /// печатаются — это рекорды, побитые после 22.09. В обычном прогоне пропускается (сеть),
    /// включается переменной SWIMM_NET_TESTS=1; коды — SWIMM_NET_COUNTRIES (через запятую).
    /// </summary>
    [Fact]
    public async Task Live_Json_MatchesSeptemberXlsxArchive()
    {
        if (Environment.GetEnvironmentVariable("SWIMM_NET_TESTS") != "1") return;

        var codes = (Environment.GetEnvironmentVariable("SWIMM_NET_COUNTRIES") ?? "AGU,RUS")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var archive = LoadArchive("worldrecords-countries__fetched-2026-09-22.csv");
        var countries = await new WorldAquaticsCountriesProvider(new LiveClientFactory()).GetCountriesAsync();
        var fetcher = new WorldAquaticsCountryFetcher(new LiveClientFactory());

        int same = 0, differ = 0, onlyJson = 0, onlyXlsx = 0, mismatched = 0;
        foreach (var code in codes)
        {
            var result = await fetcher.FetchAsync(countries.Single(c => c.Code == code));
            mismatched += result.Mismatches.Count;
            foreach (var m in result.Mismatches)
                output.WriteLine($"{code} непринята: {m.ReportedCountry} {m.Gender} {m.PoolType} {m.Style} {m.Distance} {m.Time}");

            var xlsx = Swimm.Infrastructure.Services.RecordDiffService.DeduplicateByAxes(archive
                    .Where(a => a[1] == code)
                    .Select(a => new ParsedRecordDto(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8],
                        a[9], null, a[11], a[12]))
                    .ToList())
                .ToDictionary(r => $"{r.Gender}|{r.PoolType}|{r.Style}|{r.Distance}",
                    r => new[] { r.RegionType, r.RegionCode, "", "", r.Gender, r.PoolType, r.Style, r.Distance,
                        r.Time, r.HolderName ?? "", "", r.HolderCountry ?? "", r.RecordDate ?? "" });
            // Источник отдаёт и прежний рекорд рядом с новым — дифф схлопывает их так же.
            var json = Swimm.Infrastructure.Services.RecordDiffService.DeduplicateByAxes(result.Records)
                .ToDictionary(r => $"{r.Gender}|{r.PoolType}|{r.Style}|{r.Distance}");

            foreach (var (key, r) in json)
            {
                if (!xlsx.TryGetValue(key, out var a)) { onlyJson++; output.WriteLine($"{code} только JSON: {key} {r.Time} {r.HolderName} {r.RecordDate}"); continue; }
                var holderSame = r.HolderIsTeam || a[9] == r.HolderName;
                if (a[8] == r.Time && a[12] == r.RecordDate && holderSame) { same++; continue; }
                differ++;
                output.WriteLine($"{code} {key}: {a[8]} {a[9]} {a[12]} → {r.Time} {r.HolderName} {r.RecordDate}");
            }
            foreach (var key in xlsx.Keys.Except(json.Keys))
            {
                onlyXlsx++;
                output.WriteLine($"{code} только XLSX: {key} {xlsx[key][8]} {xlsx[key][9]}");
            }
        }

        output.WriteLine($"совпало {same}, отличается {differ}, только JSON {onlyJson}, только XLSX {onlyXlsx}, непринято {mismatched}");
        Assert.True(same > 0);
    }

    private static string Key(string[] a) => $"{a[4]}|{a[5]}|{a[6]}|{a[7]}";

    /// <summary>CSV архива: RegionType,RegionCode,Category,AgeKey,Gender,PoolType,Style,Distance,Time,HolderName,Club,HolderCountry,RecordDate.</summary>
    private static List<string[]> LoadArchive(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "!records-sources"))) dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "!records-sources", file);
        return File.ReadLines(path).Skip(1).Select(SplitCsv).Where(a => a[0] == "country").ToList();
    }

    private static string[] SplitCsv(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        cells.Add(sb.ToString());
        return cells.ToArray();
    }

    private static WorldAquaticsCountryFetcher Fetcher(StubHandler handler) =>
        new(new StubFactory(handler), retryDelay: TimeSpan.Zero);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public HttpResponseMessage Respond(HttpRequestMessage request) => respond(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Настоящая сеть — только для живого теста под SWIMM_NET_TESTS=1.</summary>
    private sealed class LiveClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
