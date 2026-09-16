using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Прогон «рекорды по странам» (этап 11.1.2): мировые один раз, потом NR каждой страны,
/// в конце один дифф на весь прогон.
///
/// ⚠ Главное, что эти тесты охраняют, — **Израиль в прогон не входит**. На
/// <c>country/ISR/open</c> пишут два источника, и по решению Влада (И-13) федерация обязана
/// применяться ПОСЛЕ World Aquatics. Стоит батчу захватить ISR — и он молча откатит более
/// свежие израильские рекорды.
/// </summary>
public class RecordCountryRunTests
{
    private static readonly RecordCountryDto[] Countries =
    [
        new("AGU", "27624a51-d5fb-4800-a49a-8dd1faf7b6af", "Anguilla", "Americas"),
        new("ISR", "962f77d6-d9c0-49ad-ba93-adc831c9ec9f", "Israel", "Europe"),
        new("JAM", "2a9d0f31-7c44-4a0e-9b6d-5f1c8e2a7b04", "Jamaica", "Americas"),
        new("USA", "1dce49f3-9980-42d5-89c9-d4c6184df85a", "United States of America", "Americas"),
    ];

    [Fact]
    public async Task Run_SkipsIsrael_EvenWhenAskedExplicitly()
    {
        var (runner, fetcher) = Runner(nameof(Run_SkipsIsrael_EvenWhenAskedExplicitly));
        var status = NewStatus();

        await runner.RunAsync(status, ["AGU", "ISR"], default);

        Assert.True(status.IsraelSkipped);
        Assert.Equal(1, status.Total);
        Assert.Equal(["AGU"], fetcher.Fetched);
        // И в упавшие Израиль тоже не попадает: он не «сломался», его сюда не звали.
        Assert.Empty(status.Failed);
    }

    [Fact]
    public async Task Run_AllCountries_MeansEveryoneButIsrael()
    {
        var (runner, fetcher) = Runner(nameof(Run_AllCountries_MeansEveryoneButIsrael));
        var status = NewStatus();

        await runner.RunAsync(status, null, default);

        Assert.Equal(Countries.Length - 1, status.Total);
        Assert.Equal(status.Total, status.Done);
        Assert.DoesNotContain("ISR", fetcher.Fetched);
        Assert.Equal(["AGU", "JAM", "USA"], fetcher.Fetched.Order().ToArray());
    }

    /// <summary>
    /// 235 стран по паре файлов: что-нибудь обязательно отвалится. Прогон идёт дальше, а
    /// упавшие видны в статусе — по ним и запускается повтор (тем же эндпоинтом, своим списком).
    /// </summary>
    [Fact]
    public async Task Run_CountryFailure_DoesNotStopTheRun()
    {
        var (runner, fetcher) = Runner(nameof(Run_CountryFailure_DoesNotStopTheRun));
        fetcher.FailFor.Add("JAM");
        var status = NewStatus();

        await runner.RunAsync(status, null, default);

        var failure = Assert.Single(status.Failed);
        Assert.Equal("JAM", failure.Code);
        Assert.Contains("не скачался", failure.Error);

        Assert.Equal(3, status.Done);           // упавшая страна тоже пройдена
        Assert.NotNull(status.Diff);            // дифф всё равно построен
        Assert.Equal(2, status.Diff!.Added.Count(e => e.RegionType == "country"));
    }

    [Fact]
    public async Task Run_UnknownCode_GoesToFailedInsteadOfCrashing()
    {
        var (runner, fetcher) = Runner(nameof(Run_UnknownCode_GoesToFailedInsteadOfCrashing));
        var status = NewStatus();

        await runner.RunAsync(status, ["AGU", "ZZZ"], default);

        Assert.Equal(1, status.Total);
        Assert.Equal(["AGU"], fetcher.Fetched);
        var failure = Assert.Single(status.Failed);
        Assert.Equal("ZZZ", failure.Code);
    }

    /// <summary>Мировые — один раз на прогон: 235 стран умножили бы эти два файла на 235.</summary>
    [Fact]
    public async Task Run_FetchesWorldRecordsOnce()
    {
        var (runner, fetcher) = Runner(nameof(Run_FetchesWorldRecordsOnce));
        var status = NewStatus();

        await runner.RunAsync(status, null, default);

        Assert.Equal(1, fetcher.WorldCalls);
        Assert.True(status.WorldFetched);
        Assert.Single(status.Diff!.Added, e => e.RegionType == "world");
    }

    /// <summary>
    /// Мировые не скачались — не повод терять прогон по странам: сторож правдоподобия
    /// возьмёт эталон из базы.
    /// </summary>
    [Fact]
    public async Task Run_WorldFailure_LeavesNationalRecordsAlone()
    {
        var (runner, fetcher) = Runner(nameof(Run_WorldFailure_LeavesNationalRecordsAlone));
        fetcher.WorldFails = true;
        var status = NewStatus();

        await runner.RunAsync(status, null, default);

        Assert.False(status.WorldFetched);
        Assert.Equal("WR", Assert.Single(status.Failed).Code);
        Assert.Equal(3, status.Done);
        Assert.Equal(3, status.Diff!.Added.Count);
        Assert.DoesNotContain(status.Diff.Added, e => e.RegionType == "world");
    }

    [Fact]
    public async Task Run_CollectsMismatchesAcrossCountries()
    {
        var (runner, fetcher) = Runner(nameof(Run_CollectsMismatchesAcrossCountries));
        fetcher.MismatchFor.Add("USA");
        var status = NewStatus();

        await runner.RunAsync(status, null, default);

        Assert.Equal(1, status.MismatchCount);
        var mismatch = Assert.Single(status.Mismatches);
        Assert.Equal("USA", mismatch.RequestedCode);
    }

    /// <summary>
    /// «Нет в источнике» считается только по странам прогона. Пока страна была одна, разницы
    /// не было; при прогоне по AGU все остальные страны категории <c>open</c> (здесь — ISR)
    /// выглядели бы «пропавшими из источника», хотя их никто и не запрашивал.
    /// </summary>
    [Fact]
    public async Task Run_MissingInSource_CountsOnlyRunRegions()
    {
        var db = CreateDb(nameof(Run_MissingInSource_CountsOnlyRunRegions));
        db.Records.AddRange(
            Existing("ISR", "freestyle", "50m", "22.01"),
            Existing("ISR", "backstroke", "100m", "54.30"),
            Existing("AGU", "freestyle", "50m", "24.11"),   // источник его принесёт
            Existing("AGU", "butterfly", "200m", "2:10.44"));   // а этого — нет
        await db.SaveChangesAsync();

        var (runner, _) = Runner(db);
        var status = NewStatus();

        await runner.RunAsync(status, ["AGU"], default);

        // Ровно одна строка AGU, которой нет в источнике. Две израильские не в счёт.
        Assert.Equal(1, status.Diff!.MissingInSourceCount);
    }

    [Fact]
    public async Task Run_WithoutAnyRows_Fails()
    {
        var (runner, fetcher) = Runner(nameof(Run_WithoutAnyRows_Fails));
        fetcher.WorldFails = true;
        foreach (var c in Countries) fetcher.FailFor.Add(c.Code);
        var status = NewStatus();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(status, null, default));

        Assert.Contains("ни одной строки", ex.Message);
        Assert.Null(status.Diff);
    }

    // ── обвязка ──────────────────────────────────────────────────────────────────────

    private static RecordCountryRunStatus NewStatus() => new()
    {
        RunId = Guid.NewGuid(),
        State = RecordCountryRunState.Running,
        QueuedAt = DateTimeOffset.UtcNow,
    };

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static (RecordCountryRunner Runner, StubFetcher Fetcher) Runner(string dbName) =>
        Runner(CreateDb(dbName));

    private static (RecordCountryRunner Runner, StubFetcher Fetcher) Runner(SwimmDbContext db)
    {
        var fetcher = new StubFetcher();
        var diff = new RecordDiffService(db, new MemoryCache(new MemoryCacheOptions()));
        // Пауза ноль: в бою между странами 2 с вежливости к источнику, в тесте спать незачем.
        return (new RecordCountryRunner(new StubCountries(), fetcher, diff, TimeSpan.Zero), fetcher);
    }

    private static Swimm.Domain.Entities.Record Existing(
        string code, string style, string distance, string time) => new()
    {
        RegionType = "country",
        RegionCode = code,
        Category = "open",
        AgeKey = "",
        Gender = "female",
        PoolType = "50m",
        Style = style,
        Distance = distance,
        Time = time,
        HolderName = "Holder",
        HolderCountry = code,
        RecordDate = "20/07/2025",
        UpdatedAt = DateTime.UtcNow,
    };

    private sealed class StubCountries : IRecordCountriesProvider
    {
        public string Source => "worldrecords";

        public Task<IReadOnlyList<RecordCountryDto>> GetCountriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecordCountryDto>>(Countries);
    }

    private sealed class StubFetcher : IRecordCountryFetcher
    {
        public List<string> Fetched { get; } = [];
        public int WorldCalls { get; private set; }
        public HashSet<string> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MismatchFor { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool WorldFails { get; set; }

        public string Source => "worldrecords";

        public Task<RecordCountryFetchResult> FetchAsync(RecordCountryDto country, CancellationToken ct = default)
        {
            Fetched.Add(country.Code);

            if (FailFor.Contains(country.Code))
                throw new InvalidOperationException($"NR SCM для {country.Code} не скачался за 3 попытки");

            var records = new[]
            {
                new ParsedRecordDto("country", country.Code, "open", "", "female", "50m",
                    "freestyle", "50m", "24.11", "Holder", null, country.Code, "20/07/2025"),
            };

            var mismatches = MismatchFor.Contains(country.Code)
                ? new[] { new RecordCountryMismatchDto(country.Code, "Great Britain", "50m", "backstroke", "100m", "male", "55.02") }
                : [];

            return Task.FromResult(new RecordCountryFetchResult(country.Code, records, mismatches));
        }

        public Task<IReadOnlyList<ParsedRecordDto>> FetchWorldAsync(CancellationToken ct = default)
        {
            WorldCalls++;

            if (WorldFails)
                throw new InvalidOperationException("WR SCM не скачался за 3 попытки");

            return Task.FromResult<IReadOnlyList<ParsedRecordDto>>(
            [
                new ParsedRecordDto("world", "", "open", "", "female", "50m",
                    "freestyle", "50m", "23.61", "World Holder", null, "SWE", "27/06/2026"),
            ]);
        }
    }
}
