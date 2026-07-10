using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services;
using Swimm.Parsing.Parsers.WorldRecords;
using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Диагностика/регрессия провайдера worldrecords на РЕАЛЬНЫХ отчётах worldaquatics.
/// Файлы не коммитятся: тест читает каталог из env SWIMM_WR_REPORTS_DIR
/// (WR_SCM.xlsx, WR_LCM.xlsx, NR_SCM.xlsx, NR_LCM.xlsx — см. URL-ы в провайдере)
/// и скипается, если переменная не задана. Появился после бага приёмки 2.6:
/// Apply падал на 23505 duplicate key — источник даёт несколько строк на одну
/// дисциплину (ничьи/переустановления), дифф обязан схлопывать их до одной.
/// </summary>
public class WorldRecordsSourceProviderTests
{
    /// <summary>HttpClient, отдающий сохранённые XLSX вместо похода в интернет.</summary>
    private sealed class FileBackedHandler : HttpMessageHandler
    {
        private readonly string _dir;
        public FileBackedHandler(string dir) => _dir = dir;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var q = request.RequestUri!.Query;
            var file = q.Contains("recordCode=NR")
                ? (q.Contains("pool=SCM") ? "NR_SCM.xlsx" : "NR_LCM.xlsx")
                : (q.Contains("pool=SCM") ? "WR_SCM.xlsx" : "WR_LCM.xlsx");
            var bytes = File.ReadAllBytes(Path.Combine(_dir, file));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    [Fact]
    public async Task Fetch_RealReports_DeduplicatedByAxes_BestTimeWins()
    {
        var dir = Environment.GetEnvironmentVariable("SWIMM_WR_REPORTS_DIR");
        if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "WR_SCM.xlsx")))
            return; // нет сохранённых отчётов — скип (локальная диагностика, не CI)

        var provider = new WorldRecordsSourceProvider(
            new StubHttpClientFactory(new FileBackedHandler(dir)), new WorldRecordsParser());

        var raw = await provider.FetchAsync(new RecordSourceRequest("worldrecords", null, null, null, null, null));
        Assert.NotEmpty(raw);
        // В сыром отчёте дубли ЕСТЬ (equalled-рекорды, прогрессия времён) — их гасит дифф-сервис.
        Assert.True(raw.Count > raw.Select(AxisKey).Distinct().Count(),
            "Ожидались дубли в сыром отчёте — источник поменял формат? Перепроверь необходимость DeduplicateByAxes.");

        var parsed = RecordDiffService.DeduplicateByAxes(raw);

        // Ключ = 8 осей unique-индекса Records — дубль здесь означает 23505 на Apply.
        var dupes = parsed
            .GroupBy(AxisKey)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} x{g.Count()}: {string.Join(" / ", g.Select(x => $"{x.Time} {x.HolderName}"))}")
            .ToList();
        Assert.True(dupes.Count == 0, $"Дубли по осям ({dupes.Count}):\n{string.Join("\n", dupes)}");

        // Из нескольких времён одной дисциплины остаётся ЛУЧШЕЕ (наименьшее).
        foreach (var g in raw.GroupBy(AxisKey).Where(g => g.Count() > 1))
        {
            var kept = parsed.Single(p => AxisKey(p) == g.Key);
            Assert.Equal(g.Min(p => TimeToCs(p.Time)), TimeToCs(kept.Time));
        }
    }

    private static string AxisKey(ParsedRecordDto p) =>
        string.Join("|", p.RegionType, p.RegionCode, p.Category, p.AgeKey, p.Gender, p.PoolType, p.Style, p.Distance);

    private static long TimeToCs(string time) =>
        time.Split(':').Aggregate(0L, (acc, part) =>
            acc * 60 + (long)Math.Round(decimal.Parse(part, System.Globalization.CultureInfo.InvariantCulture) * 100));
}
