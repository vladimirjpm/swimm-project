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
/// Оборванный прогон по странам продолжается, а не начинается заново (И-68). 16.11.2026
/// ProdAdmin закрылся на стране 103 из 214 — полтора часа запросов пропали вместе с процессом.
/// Теперь страна ложится на диск сразу после скачивания, и следующий прогон по всем странам
/// ходит в источник только за остальными.
/// </summary>
public class RecordCountryRunResumeTests
{
    private static readonly RecordCountryDto[] Countries =
    [
        new("AGU", "27624a51-d5fb-4800-a49a-8dd1faf7b6af", "Anguilla", "Americas"),
        new("ISR", "962f77d6-d9c0-49ad-ba93-adc831c9ec9f", "Israel", "Europe"),
        new("JAM", "2a9d0f31-7c44-4a0e-9b6d-5f1c8e2a7b04", "Jamaica", "Americas"),
        new("USA", "1dce49f3-9980-42d5-89c9-d4c6184df85a", "United States of America", "Americas"),
    ];

    [Fact]
    public async Task FullRun_SavesEveryCountryAsItGoes_AndClearsWhenAllPassed()
    {
        var progress = new MemoryProgress();
        var (runner, _) = Runner(nameof(FullRun_SavesEveryCountryAsItGoes_AndClearsWhenAllPassed), progress);

        await runner.RunAsync(NewStatus(), null, default);

        Assert.Equal(1, progress.Begins);
        Assert.Equal(["AGU", "JAM", "USA"], progress.SavedCodes.Order().ToArray());
        Assert.True(progress.WorldSaved);
        // Прошли все: следующий прогон обязан снова сходить в источник.
        Assert.Equal(1, progress.Clears);
        Assert.Null(progress.Current);
    }

    /// <summary>Главный сценарий: процесс умер посреди прогона — новый прогон докачивает остальное.</summary>
    [Fact]
    public async Task FullRun_AfterInterruption_FetchesOnlyTheRest()
    {
        var progress = new MemoryProgress();
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);
        progress.Current = new RecordCountryRunSaved(startedAt, [World()],
            new Dictionary<string, RecordCountryFetchResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["AGU"] = new("AGU", [Nr("AGU")], [new RecordCountryMismatchDto("AGU", "GBR", "50m", "backstroke", "100m", "male", "55.02")]),
            });

        var (runner, fetcher) = Runner(nameof(FullRun_AfterInterruption_FetchesOnlyTheRest), progress);
        var status = NewStatus();

        await runner.RunAsync(status, null, default);

        Assert.Equal(["JAM", "USA"], fetcher.Fetched.Order().ToArray());
        Assert.Equal(0, fetcher.WorldCalls);          // мировые тоже взяты из сохранённого
        Assert.Equal(0, progress.Begins);             // продолжаем, а не начинаем заново

        Assert.Equal(1, status.Resumed);
        Assert.Equal(startedAt, status.ResumedFrom);
        Assert.Equal(3, status.Done);
        Assert.True(status.WorldFetched);
        Assert.Equal(1, status.MismatchCount);        // непринятые сохранённой страны не теряются

        // Дифф — по ВСЕМ странам: сохранённая AGU в нём есть наравне со скачанными.
        Assert.Equal(3, status.Diff!.Added.Count(e => e.RegionType == "country"));
        Assert.Single(status.Diff.Added, e => e.RegionType == "world");
    }

    /// <summary>Упала страна — сохранённое держим: следующий прогон докачает только её.</summary>
    [Fact]
    public async Task FullRun_WithFailure_KeepsSavedForTheNextRun()
    {
        var progress = new MemoryProgress();
        var (runner, fetcher) = Runner(nameof(FullRun_WithFailure_KeepsSavedForTheNextRun), progress);
        fetcher.FailFor.Add("JAM");

        await runner.RunAsync(NewStatus(), null, default);

        Assert.Equal(0, progress.Clears);
        Assert.Equal(["AGU", "USA"], progress.SavedCodes.Order().ToArray());

        // Второй прогон: JAM ожила — в источник идём только за ней.
        fetcher.FailFor.Clear();
        fetcher.Fetched.Clear();
        var second = NewStatus();
        await runner.RunAsync(second, null, default);

        Assert.Equal(["JAM"], fetcher.Fetched);
        Assert.Equal(2, second.Resumed);
        Assert.Empty(second.Failed);
        Assert.Equal(1, progress.Clears);
    }

    /// <summary>Мировые упали — их и докачиваем, страны берём из сохранённого.</summary>
    [Fact]
    public async Task FullRun_WorldFailed_KeepsSaved()
    {
        var progress = new MemoryProgress();
        var (runner, fetcher) = Runner(nameof(FullRun_WorldFailed_KeepsSaved), progress);
        fetcher.WorldFails = true;

        await runner.RunAsync(NewStatus(), null, default);

        Assert.Equal(0, progress.Clears);
        Assert.False(progress.WorldSaved);
    }

    /// <summary>
    /// Подмножество кодов — отладка: в источник честно за каждой страной, сохранённое не
    /// читаем и не трогаем (иначе отладочный прогон стёр бы сохранённое полного).
    /// </summary>
    [Fact]
    public async Task SubsetRun_NeitherReadsNorWritesSaved()
    {
        var progress = new MemoryProgress
        {
            Current = new RecordCountryRunSaved(DateTimeOffset.UtcNow, [World()],
                new Dictionary<string, RecordCountryFetchResult> { ["AGU"] = new("AGU", [Nr("AGU")], []) }),
        };
        var (runner, fetcher) = Runner(nameof(SubsetRun_NeitherReadsNorWritesSaved), progress);
        var status = NewStatus();

        await runner.RunAsync(status, ["AGU"], default);

        Assert.Equal(["AGU"], fetcher.Fetched);
        Assert.Equal(0, status.Resumed);
        Assert.Equal(0, progress.Loads + progress.Begins + progress.Clears);
        Assert.Empty(progress.SavedCodes);
        Assert.NotNull(progress.Current);
    }

    // ── обвязка ──────────────────────────────────────────────────────────────────────

    private static RecordCountryRunStatus NewStatus() => new()
    {
        RunId = Guid.NewGuid(),
        State = RecordCountryRunState.Running,
        QueuedAt = DateTimeOffset.UtcNow,
    };

    private static (RecordCountryRunner Runner, StubFetcher Fetcher) Runner(string dbName, IRecordCountryRunProgress progress)
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var fetcher = new StubFetcher();
        var diff = new RecordDiffService(db, new MemoryCache(new MemoryCacheOptions()));
        return (new RecordCountryRunner(new StubCountries(), fetcher, diff, null, TimeSpan.Zero, progress), fetcher);
    }

    private static ParsedRecordDto Nr(string code) =>
        new("country", code, "open", "", "female", "50m", "freestyle", "50m", "24.11", "Holder", null, code, "20/07/2025");

    private static ParsedRecordDto World() =>
        new("world", "", "open", "", "female", "50m", "freestyle", "50m", "23.61", "World Holder", null, "SWE", "27/06/2026");

    /// <summary>Сохранённое в памяти — с тем же договором, что у файловой реализации.</summary>
    private sealed class MemoryProgress : IRecordCountryRunProgress
    {
        public RecordCountryRunSaved? Current { get; set; }
        public int Loads { get; private set; }
        public int Begins { get; private set; }
        public int Clears { get; private set; }
        public bool WorldSaved { get; private set; }
        public List<string> SavedCodes { get; } = [];

        public Task<RecordCountryRunSaved?> LoadAsync(string source, CancellationToken ct = default)
        {
            Loads++;
            return Task.FromResult(Current);
        }

        public Task BeginAsync(string source, CancellationToken ct = default)
        {
            Begins++;
            Current = new RecordCountryRunSaved(DateTimeOffset.UtcNow, null,
                new Dictionary<string, RecordCountryFetchResult>(StringComparer.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task SaveWorldAsync(IReadOnlyList<ParsedRecordDto> rows, CancellationToken ct = default)
        {
            WorldSaved = true;
            Current = Current! with { World = rows };
            return Task.CompletedTask;
        }

        public Task SaveCountryAsync(RecordCountryFetchResult result, CancellationToken ct = default)
        {
            SavedCodes.Add(result.Code);
            var countries = new Dictionary<string, RecordCountryFetchResult>(Current!.Countries, StringComparer.OrdinalIgnoreCase)
            {
                [result.Code] = result,
            };
            Current = Current with { Countries = countries };
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct = default)
        {
            Clears++;
            Current = null;
            return Task.CompletedTask;
        }
    }

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
        public bool WorldFails { get; set; }

        public string Source => "worldrecords";

        public Task<RecordCountryFetchResult> FetchAsync(RecordCountryDto country, CancellationToken ct = default)
        {
            Fetched.Add(country.Code);
            if (FailFor.Contains(country.Code))
                throw new InvalidOperationException($"NR для {country.Code} не скачался");
            return Task.FromResult(new RecordCountryFetchResult(country.Code, [Nr(country.Code)], []));
        }

        public Task<IReadOnlyList<ParsedRecordDto>> FetchWorldAsync(CancellationToken ct = default)
        {
            WorldCalls++;
            if (WorldFails) throw new InvalidOperationException("WR не скачался");
            return Task.FromResult<IReadOnlyList<ParsedRecordDto>>([World()]);
        }
    }
}
