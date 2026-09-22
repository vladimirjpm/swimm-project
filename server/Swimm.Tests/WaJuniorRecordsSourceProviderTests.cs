using System.Net;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services;
using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Провайдер мировых юниорских рекордов (<c>wa-junior</c>, WJR-план J1) на НАСТОЯЩИХ ответах
/// JSON-выдачи World Aquatics, снятых на J0 (21.09.2026; оригиналы — в <c>!records-sources/</c>).
///
/// Файл мужчин LCM выбран нарочно: в нём сразу все особенности источника — эстафеты,
/// повторённый рекорд (ANDREW 21.75 дважды + SHEREMET 21.75) и побитый рекорд, оставшийся в
/// выдаче (200 спина: 1:54.87 и 1:55.14), хотя запрос был «только действующие».
/// </summary>
public class WaJuniorRecordsSourceProviderTests
{
    private static Stream Fixture(string name) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RecordSources", name));

    [Fact]
    public void Parse_MenLcm_WritesWorldJuniorAxisWithBand()
    {
        var result = WaJuniorRecordsSourceProvider.Parse(Fixture("wa-junior-m-lcm.json"));

        Assert.NotEmpty(result.Records);
        Assert.All(result.Records, r =>
        {
            Assert.Equal("world", r.RegionType);
            Assert.Equal("", r.RegionCode);
            Assert.Equal("junior", r.Category);
            // Полоса — в ключе (решение 21.09.2026), а не пустая строка.
            Assert.Equal("15-18", r.AgeKey);
            Assert.Equal("male", r.Gender);
            Assert.Equal("50m", r.PoolType);
        });
    }

    /// <summary>
    /// Э1: однополые эстафеты пишутся в форме WR — «4X100m», стиль без слова Relay, полоса
    /// юниоров, держатель — команда. В файле пять эстафет (3 × 4×100 в/с, 4×200 в/с, 4×100 комплекс).
    /// </summary>
    [Fact]
    public void Parse_Relays_WrittenInWrShape()
    {
        var result = WaJuniorRecordsSourceProvider.Parse(Fixture("wa-junior-m-lcm.json"));

        Assert.Equal(0, result.SkippedRelays);
        var relays = result.Records.Where(r => r.Distance.StartsWith("4X", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, relays.Count);
        Assert.All(relays, r =>
        {
            Assert.Equal("male", r.Gender);
            Assert.Equal(WaJuniorRecordsSourceProvider.MaleBand, r.AgeKey);
            Assert.Equal("50m", r.PoolType);
        });
        Assert.Equal(3, relays.Count(r => r is { Style: "freestyle", Distance: "4X100m" }));
        var fr200 = Assert.Single(relays, r => r is { Style: "freestyle", Distance: "4X200m" });
        Assert.Equal("07:08.37", fr200.Time);
        Assert.Equal("USA", fr200.HolderCountry);
        var medley = Assert.Single(relays, r => r.Style == "individual_medley");
        Assert.Equal("4X100m", medley.Distance);
        Assert.Equal("03:33.19", medley.Time);
        Assert.False(string.IsNullOrEmpty(medley.HolderName));
    }

    /// <summary>
    /// Э3: смешанные эстафеты (запрос gender=X, disciplineGender=2) — пол mixed, полоса 14-18.
    /// Ответы источника 22.09.2026: LCM — три строки (два 4×100 в/с, 4×100 комплекс), SCM — одна.
    /// </summary>
    [Fact]
    public void Parse_MixedRelays_GenderMixedBandUnion()
    {
        var lcm = WaJuniorRecordsSourceProvider.Parse(Fixture("wa-junior-x-lcm.json"));
        var scm = WaJuniorRecordsSourceProvider.Parse(Fixture("wa-junior-x-scm.json"));

        Assert.Equal(0, lcm.SkippedRelays + scm.SkippedRelays);
        Assert.Equal(3, lcm.Records.Count);
        Assert.All(lcm.Records.Concat(scm.Records), r =>
        {
            Assert.Equal("mixed", r.Gender);
            Assert.Equal(WaJuniorRecordsSourceProvider.MixedBand, r.AgeKey);
            Assert.StartsWith("4X", r.Distance);
        });
        var medley50 = Assert.Single(scm.Records);
        Assert.Equal(("25m", "individual_medley", "4X50m", "01:41.21"),
            (medley50.PoolType, medley50.Style, medley50.Distance, medley50.Time));
    }

    /// <summary>
    /// Сырой провайдер отдаёт ВСЕ строки источника — дубли гасит дифф, как у WR. После него на
    /// 200 спину остаётся новый рекорд, а не побитый.
    /// </summary>
    [Fact]
    public void Dedup_BrokenRecordStillInFeed_BestTimeWins()
    {
        var raw = WaJuniorRecordsSourceProvider.Parse(Fixture("wa-junior-m-lcm.json")).Records;

        var back200 = raw.Where(r => r.Style == "backstroke" && r.Distance == "200m").ToList();
        Assert.Equal(2, back200.Count);

        var deduped = RecordDiffService.DeduplicateByAxes(raw);
        var best = Assert.Single(deduped, r => r.Style == "backstroke" && r.Distance == "200m");
        Assert.Equal("01:54.87", best.Time);
        Assert.Equal("11/08/2026", best.RecordDate);

        // 17 личных дисциплин длинной воды + 3 эстафеты (4×100 в/с — три строки → одна) —
        // по одной строке на каждую.
        Assert.Equal(20, deduped.Count);
        Assert.Equal("03:12.75", Assert.Single(deduped, r => r is { Style: "freestyle", Distance: "4X100m" }).Time);
    }

    /// <summary>Совместный рекорд: держатели склеиваются, один и тот же человек — один раз.</summary>
    [Fact]
    public void Dedup_EqualledRecord_JointHoldersOnce()
    {
        var raw = WaJuniorRecordsSourceProvider.Parse(Fixture("wa-junior-m-lcm.json")).Records;

        var free50 = Assert.Single(RecordDiffService.DeduplicateByAxes(raw),
            r => r.Style == "freestyle" && r.Distance == "50m");

        Assert.Equal("21.75", free50.Time);
        Assert.Equal("Michael Andrew, Nikita Sheremet", free50.HolderName);
    }

    [Fact]
    public void Parse_WomenScm_BandAndShortCourseMedley()
    {
        var records = WaJuniorRecordsSourceProvider.Parse(Fixture("wa-junior-f-scm.json")).Records;

        Assert.All(records, r =>
        {
            Assert.Equal("14-17", r.AgeKey);
            Assert.Equal("female", r.Gender);
            Assert.Equal("25m", r.PoolType);
        });
        // В короткой воде есть 100 комплекс, которого нет в длинной — 18 дисциплин.
        Assert.Contains(records, r => r.Style == "individual_medley" && r.Distance == "100m");
        Assert.Equal(18, RecordDiffService.DeduplicateByAxes(records).Count);
    }

    /// <summary>Форма времени, имени и даты — та же, что у world/open: они стоят рядом в карточке.</summary>
    [Fact]
    public void Parse_FormatsMatchWorldOpen()
    {
        var records = WaJuniorRecordsSourceProvider.Parse(Fixture("wa-junior-m-lcm.json")).Records;

        // Во входе «time» = «00:00:24» без долей — берётся timeFormatted «24.00».
        Assert.Contains(records, r => r.Time == "24.00");
        Assert.DoesNotContain(records, r => r.Time.StartsWith("00:", StringComparison.Ordinal));
        Assert.All(records, r => Assert.Matches(@"^\d{2}/\d{2}/\d{4}$", r.RecordDate!));
        Assert.All(records, r => Assert.Matches("^[A-Z]{3}$", r.HolderCountry!));
    }

    [Theory]
    [InlineData("Claire", "CURZAN", "Claire Curzan")]
    [InlineData("Summer", "McINTOSH", "Summer McINTOSH")]           // смешанный регистр не трогаем
    [InlineData("Kaylee", "SMITH-JONES", "Kaylee Smith-Jones")]
    [InlineData("Ana", "DE LA ROSA", "Ana De La Rosa")]
    [InlineData("", "LEDECKY", "Ledecky")]
    public void HolderName_TitleCasesUpperLastName(string first, string last, string expected) =>
        Assert.Equal(expected, WaJuniorRecordsSourceProvider.HolderName(first, last));

    /// <summary>
    /// Выдача стала постраничной — падаем, а не пишем половину справочника (тогда дифф показал
    /// бы вторую половину «пропавшей из источника»).
    /// </summary>
    [Fact]
    public void Parse_TruncatedFeed_Throws()
    {
        var json = """{ "totalRowCount": 30, "records": [] }""";
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WaJuniorRecordsSourceProvider.Parse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))));
        Assert.Contains("постраничной", ex.Message);
    }

    /// <summary>
    /// Fetch по сети: четыре запроса (пол × бассейн) к JSON-выдаче, только на whitelisted-хост;
    /// один 504 переживает повтором.
    /// </summary>
    [Fact]
    public async Task Fetch_SixQueries_RetriesOnce504()
    {
        var handler = new StubHandler();
        var provider = new WaJuniorRecordsSourceProvider(new StubFactory(handler));

        var records = await provider.FetchAsync(new RecordSourceRequest("wa-junior"));

        Assert.NotEmpty(records);
        var distinct = handler.Requests.Distinct().ToList();
        Assert.Equal(6, distinct.Count); // F, M, X (Э3) × LCM, SCM
        Assert.All(distinct, u =>
        {
            Assert.StartsWith($"https://{WorldAquaticsSource.ApiHost}/fina/records/SW?", u);
            Assert.Contains("recordCode=WJ", u);
        });
        // Первый запрос F LCM получил 504 и был повторён.
        Assert.Equal(7, handler.Requests.Count);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        private bool _failedOnce;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests) Requests.Add(url);

            if (url.Contains("gender=F&pool=LCM") && !_failedOnce)
            {
                _failedOnce = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.GatewayTimeout));
            }

            // Отдаём один и тот же реальный файл: здесь проверяется транспорт, не разбор.
            var bytes = File.ReadAllBytes(Path.Combine(
                AppContext.BaseDirectory, "Fixtures", "RecordSources", "wa-junior-m-lcm.json"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
