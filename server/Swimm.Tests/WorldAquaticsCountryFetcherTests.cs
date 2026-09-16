using System.Net;
using System.Net.Http;
using ClosedXML.Excel;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers.WorldRecords;
using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Национальные рекорды одной страны по GUID (этап 11.1.3 п. 2). Отчёты источника
/// подделываются в памяти ClosedXML с теми же заголовками, что у настоящих XLSX
/// (Event, Pool, Record, Time, Athlete, NF Code, Gender, Competition, Country, City, Date) —
/// сеть в тестах запрещена.
/// </summary>
public class WorldAquaticsCountryFetcherTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly RecordCountryDto Anguilla =
        new("AGU", "27624a51-d5fb-4800-a49a-8dd1faf7b6af", "Anguilla", "Americas");

    /// <summary>Два файла страны: SCM с четырьмя строками и LCM с одной.</summary>
    private static StubHandler TwoPoolReports() => new(
        Ok(Report("SCM",
            // Обычная строка страны. Колонка Country — «Great Britain» СПЕЦИАЛЬНО: это страна
            // места соревнования, и пока NF Code на месте, её никто не должен спрашивать.
            ("Women's 50m Freestyle", "NR", "24.11", "SMITH Jane", "AGU", "W", "Great Britain"),
            // NF Code пуст → парсер подставит туда страну МЕСТА соревнования. Такую строку
            // в режиме «все страны» писать нельзя (план §3а).
            ("Men's 100m Backstroke", "NR", "55.02", "BROWN Bob", "", "M", "Great Britain"),
            // Чужая федерация в отчёте страны — тоже в отчёт, а не в базу.
            ("Women's 200m Butterfly", "NR", "2:10.44", "DOE Ann", "USA", "W", "Anguilla"),
            // Микст-эстафета вне модели осей Record — молча мимо, это не находка.
            ("Mixed 4x100m Freestyle Relay", "NR", "3:30.01", "", "AGU", "X", "Anguilla"))),
        Ok(Report("LCM",
            ("Men's 200m Breaststroke", "NR, WR", "2:05.48", "ROE Rob", "AGU", "M", "Anguilla"))));

    [Fact]
    public async Task FetchAsync_TakesRegionCodeFromRequest()
    {
        var handler = TwoPoolReports();

        var result = await Fetcher(handler).FetchAsync(Anguilla);

        Assert.Equal("AGU", result.Code);
        Assert.Equal(2, result.Records.Count);
        Assert.All(result.Records, r =>
        {
            Assert.Equal("country", r.RegionType);
            Assert.Equal("AGU", r.RegionCode);
            Assert.Equal("AGU", r.HolderCountry);
            // age и masters остаются исключительно израильскими (решение Влада 29.07.2026).
            Assert.Equal("open", r.Category);
            Assert.Equal("", r.AgeKey);
        });

        // Бассейн — из колонки Pool каждой строки: SCM = 25m, LCM = 50m.
        var free = Assert.Single(result.Records, r => r.Style == "freestyle");
        Assert.Equal("25m", free.PoolType);
        Assert.Equal("50m", free.Distance);
        Assert.Equal("female", free.Gender);
        Assert.Equal("24.11", free.Time);

        // «NR, WR» — национальный рекорд, который заодно мировой. Тип рекорда схлопывается
        // в WR, и если бы код региона брался из него, строка потерялась бы.
        var breast = Assert.Single(result.Records, r => r.Style == "breaststroke");
        Assert.Equal("50m", breast.PoolType);
        Assert.Equal("200m", breast.Distance);
        Assert.Equal("AGU", breast.RegionCode);
    }

    [Fact]
    public async Task FetchAsync_ReportsForeignAndEmptyNfCode()
    {
        var handler = TwoPoolReports();

        var result = await Fetcher(handler).FetchAsync(Anguilla);

        Assert.Equal(2, result.Mismatches.Count);
        Assert.All(result.Mismatches, m => Assert.Equal("AGU", m.RequestedCode));

        // Пустой NF Code виден в отчёте ровно тем, что подставил парсер, — названием страны
        // места соревнования. Именно так его и надо показывать админу.
        var empty = Assert.Single(result.Mismatches, m => m.Style == "backstroke");
        Assert.Equal("Great Britain", empty.ReportedCountry);
        Assert.Equal("100m", empty.Distance);
        Assert.Equal("male", empty.Gender);
        Assert.Equal("55.02", empty.Time);
        Assert.Equal("25m", empty.PoolType);

        var foreign = Assert.Single(result.Mismatches, m => m.Style == "butterfly");
        Assert.Equal("USA", foreign.ReportedCountry);

        // Ни одна отброшенная строка не должна была просочиться под кодом AGU.
        Assert.DoesNotContain(result.Records, r => r.Style is "backstroke" or "butterfly");
        // Микст-эстафета — не находка: она вне модели, а не «чужая страна».
        Assert.DoesNotContain(result.Mismatches, m => m.Gender == "mix");
    }

    [Fact]
    public async Task FetchAsync_AsksBothPoolsOfOneCountry()
    {
        var handler = TwoPoolReports();

        await Fetcher(handler).FetchAsync(Anguilla);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, url =>
        {
            Assert.StartsWith($"https://{WorldAquaticsSource.ApiHost}/fina/records/report?", url);
            Assert.Contains("recordCode=NR", url);
            Assert.Contains($"countryId={Anguilla.SourceId}", url);
        });
        Assert.Contains(handler.Requests, u => u.Contains("pool=SCM"));
        Assert.Contains(handler.Requests, u => u.Contains("pool=LCM"));

        // Мировые рекорды — один раз на прогон, а не на страну (11.1.2).
        Assert.DoesNotContain(handler.Requests, u => u.Contains("recordCode=WR"));
    }

    /// <summary>
    /// Прогон идёт час-два по 235 странам: 503 на одном файле не повод терять страну.
    /// </summary>
    [Fact]
    public async Task FetchAsync_RetriesServerErrors()
    {
        var handler = new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            Ok(Report("SCM", ("Women's 50m Freestyle", "NR", "24.11", "SMITH Jane", "AGU", "W", "Anguilla"))));

        var result = await Fetcher(handler).FetchAsync(Anguilla);

        // Два повтора на первом файле + успешный второй файл.
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(2, result.Records.Count);   // один и тот же отчёт отдан на оба бассейна
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

    /// <summary>Кривой GUID источник не «починит» — повторять три раза по две минуты незачем.</summary>
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
        var handler = new StubHandler(Ok(Report("SCM")));
        var broken = Anguilla with { SourceId = "../../etc" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetcher(handler).FetchAsync(broken));

        Assert.Contains("GUID", ex.Message);
        Assert.Empty(handler.Requests);   // в сеть с таким идентификатором не ходим вовсе
    }

    /// <summary>
    /// Живая проверка на крошечной федерации (Ангилья, ~18 строк на оба бассейна): источник
    /// отдаёт NR по GUID, и во всех строках NF Code совпадает с запрошенной страной — то, на
    /// чём стоит решение «RegionCode из запроса». В обычном прогоне пропускается (сеть),
    /// включается переменной SWIMM_NET_TESTS=1.
    /// </summary>
    [Fact]
    public async Task Live_Anguilla_FetchesNationalRecords()
    {
        if (Environment.GetEnvironmentVariable("SWIMM_NET_TESTS") != "1") return;

        var fetcher = new WorldAquaticsCountryFetcher(new LiveClientFactory(), new WorldRecordsParser());
        var result = await fetcher.FetchAsync(Anguilla);

        output.WriteLine($"AGU: {result.Records.Count} рекордов, {result.Mismatches.Count} непринятых строк");

        Assert.NotEmpty(result.Records);
        Assert.All(result.Records, r =>
        {
            Assert.Equal("AGU", r.RegionCode);
            Assert.Equal("open", r.Category);
            Assert.Contains(r.PoolType, new[] { "25m", "50m" });
        });
        Assert.Empty(result.Mismatches);
    }

    private static WorldAquaticsCountryFetcher Fetcher(StubHandler handler) =>
        new(new StubFactory(handler), new WorldRecordsParser(), retryDelay: TimeSpan.Zero);

    private static Func<HttpRequestMessage, HttpResponseMessage> Ok(byte[] body) =>
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    /// <summary>Отчёт источника в памяти: те же колонки и та же форма значений, что в живых XLSX.</summary>
    private static byte[] Report(
        string pool,
        params (string Event, string Record, string Time, string Athlete, string NfCode, string Gender, string Country)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Records");

        string[] headers =
            ["Event", "Pool", "Record", "Time", "Athlete", "NF Code", "Gender", "Competition", "Country", "City", "Date"];
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        var rowNo = 2;
        foreach (var r in rows)
        {
            ws.Cell(rowNo, 1).Value = r.Event;
            ws.Cell(rowNo, 2).Value = pool;
            ws.Cell(rowNo, 3).Value = r.Record;
            ws.Cell(rowNo, 4).Value = r.Time;
            ws.Cell(rowNo, 5).Value = r.Athlete;
            ws.Cell(rowNo, 6).Value = r.NfCode;
            ws.Cell(rowNo, 7).Value = r.Gender;
            ws.Cell(rowNo, 8).Value = "World Championships";
            ws.Cell(rowNo, 9).Value = r.Country;
            ws.Cell(rowNo, 10).Value = "The Valley";
            ws.Cell(rowNo, 11).Value = new DateTime(2025, 7, 20);
            rowNo++;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Отдаёт ответы по очереди; последний повторяется — так один отчёт покрывает оба бассейна.</summary>
    private sealed class StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            var next = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return Task.FromResult(next(request));
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
