using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Services;
using Swimm.Parsing.Parsers.WaMastersRecords;
using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Разбор РЕАЛЬНЫХ файлов «Masters World Records» (личные дистанции). Файлы не коммитятся —
/// около мегабайта каждый и обновляются раз в месяц, поэтому тест читает каталог из env
/// <c>SWIMM_WA_MASTERS_DIR</c> и скипается, если её нет. Взять свежие:
/// <see cref="WorldAquaticsSource.MastersRecordsPageUrl"/> → «Masters World Records - LCM/SCM».
///
/// Проверяем то, что ломает импорт молча: дубли по восьми осям (Apply упал бы на 23505),
/// нераспарсиваемое время (сторож правдоподобия такие строки пропускает мимо правил) и
/// потерю полос — у столетних записей по одной на полосу, их легко срезать порогами.
/// </summary>
public class WaMastersRecordsSourceProviderTests
{
    private sealed class NoNetworkHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("Тест обязан работать на приложенных файлах, а не в сети.");
    }

    private static IReadOnlyList<ParsedRecordDto>? FetchLocal()
    {
        var dir = Environment.GetEnvironmentVariable("SWIMM_WA_MASTERS_DIR");
        if (string.IsNullOrEmpty(dir)) return null;

        var lcm = Path.Combine(dir, "CurrentWorldRecords-Individual-LCM.pdf");
        var scm = Path.Combine(dir, "CurrentWorldRecords-Individual-SCM.pdf");
        if (!File.Exists(lcm) || !File.Exists(scm)) return null;

        var factory = new NoNetworkHttpClientFactory();
        var provider = new WaMastersRecordsSourceProvider(
            factory, new WaMastersRecordsParser(), new WaMastersRecordsPageResolver(factory));

        using var primary = File.OpenRead(lcm);
        using var secondary = File.OpenRead(scm);

        return provider.FetchAsync(new RecordSourceRequest(
            "wa-masters", primary, "lcm.pdf", secondary, "scm.pdf")).GetAwaiter().GetResult();
    }

    [Fact]
    public void Fetch_RealPdfs_CoversBothPoolsAndAllAgeBands()
    {
        var parsed = FetchLocal();
        if (parsed is null) return;   // нет сохранённых файлов — скип (локальная диагностика, не CI)

        Assert.True(parsed.Count > 1000, $"Ожидалось больше 1000 личных рекордов, получено {parsed.Count}.");
        Assert.All(parsed, p => Assert.Equal("world", p.RegionType));
        Assert.All(parsed, p => Assert.Equal("masters", p.Category));
        Assert.All(parsed, p => Assert.Equal("", p.RegionCode));

        Assert.Equal(["25m", "50m"], parsed.Select(p => p.PoolType).Distinct().Order().ToArray());
        Assert.Equal(["female", "male"], parsed.Select(p => p.Gender).Distinct().Order().ToArray());

        // Полосы идут пятилетками от 25-29; верхняя зависит от того, кто ещё плавает.
        var bands = parsed.Select(p => p.AgeKey).Distinct().ToList();
        Assert.Contains("25-29", bands);
        Assert.Contains("90-94", bands);
        Assert.True(bands.Count >= 15, $"Ожидалось не меньше 15 возрастных полос, получено {bands.Count}.");
    }

    /// <summary>
    /// Дубль по восьми осям роняет Apply на unique-индексе. У источника его быть не должно:
    /// в справочнике текущих рекордов одна строка на дисциплину (прогрессия — отдельный файл).
    /// </summary>
    [Fact]
    public void Fetch_RealPdfs_NoDuplicateAxisKeys()
    {
        var parsed = FetchLocal();
        if (parsed is null) return;

        var dupes = parsed
            .GroupBy(p => string.Join('|', p.RegionType, p.RegionCode, p.Category, p.AgeKey,
                p.Gender, p.PoolType, p.Style, p.Distance))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(dupes);
    }

    /// <summary>
    /// Каждое время обязано разбираться <see cref="SwimTime"/>: иначе строка невидима и для
    /// сторожа правдоподобия, и для будущего рейтинга. Отдельный случай — столетние: их
    /// 1500 вольным идёт больше часа и печатается как «01:14:08.7».
    /// </summary>
    [Fact]
    public void Fetch_RealPdfs_EveryTimeParses()
    {
        var parsed = FetchLocal();
        if (parsed is null) return;

        var unparsed = parsed.Where(p => SwimTime.ParseToMs(p.Time) is null).ToList();
        Assert.Empty(unparsed.Select(p => $"{p.AgeKey} {p.Gender} {p.PoolType} {p.Style} {p.Distance}: '{p.Time}'"));

        var sprint = parsed.Single(p =>
            p.AgeKey == "25-29" && p.Gender == "female" && p.PoolType == "50m"
            && p.Style == "freestyle" && p.Distance == "50m");
        Assert.InRange(SwimTime.ParseToMs(sprint.Time)!.Value, 20_000, 35_000);
        Assert.False(string.IsNullOrWhiteSpace(sprint.HolderName));
        Assert.Equal(3, sprint.HolderCountry!.Length);
        Assert.Matches(@"^\d{2}/\d{2}/\d{4}$", sprint.RecordDate!);
    }

    /// <summary>
    /// Мастерский мировой рекорд не может быть быстрее абсолютного мирового. Это же правило
    /// стоит сторожем на импорте (<see cref="RecordPlausibility"/>) для страны и возраста —
    /// здесь проверяем сам источник, пока его строки ещё не в базе.
    /// </summary>
    [Fact]
    public void Fetch_RealPdfs_SlowerThanOpenWorldRecords()
    {
        var parsed = FetchLocal();
        if (parsed is null) return;

        // Абсолютные мировые рекорды на сентябрь 2026 — верхняя граница правдоподобия.
        var openWorld = new Dictionary<string, int>
        {
            ["female|50m|freestyle|50m"] = 23_610,   // 23.61
            ["male|50m|freestyle|50m"] = 20_910,     // 20.91
            ["female|25m|freestyle|50m"] = 22_930,   // 22.93
            ["male|25m|freestyle|50m"] = 19_900,     // 19.90
        };

        foreach (var (key, worldMs) in openWorld)
        {
            var parts = key.Split('|');
            var fastest = parsed
                .Where(p => p.Gender == parts[0] && p.PoolType == parts[1]
                            && p.Style == parts[2] && p.Distance == parts[3])
                .Select(p => SwimTime.ParseToMs(p.Time))
                .Where(ms => ms is not null)
                .Min();

            Assert.True(fastest > worldMs,
                $"{key}: мастерский рекорд {fastest} мс быстрее мирового {worldMs} мс — разъехались оси.");
        }
    }

    /// <summary>
    /// Дифф обязан принять эти строки как есть: схлопывание по осям не должно ничего терять,
    /// потому что дублей в источнике нет (см. выше) — проверяем, что и после него счёт тот же.
    /// </summary>
    [Fact]
    public void Deduplicate_RealPdfs_ChangesNothing()
    {
        var parsed = FetchLocal();
        if (parsed is null) return;

        Assert.Equal(parsed.Count, RecordDiffService.DeduplicateByAxes(parsed).Count);
    }
}
