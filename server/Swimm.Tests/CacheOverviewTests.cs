using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сводка кэша для админки — верх /Admin/Cache и блок «Кэш» дашборда (docs/admin-pages/cache.md):
/// размер записей, последний общий сброс, разбивка «ответы API / данные».
///
/// Главное утверждение — производительность (решение Влада 15.09.2026): путь посетителя за размер
/// не платит. Готовый ответ знает размер сам и не сериализуется; запись данных меряется при
/// взгляде админки и ровно один раз.
/// </summary>
public class CacheOverviewTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static MemoryCacheService NewCache() => new(new MemoryCache(new MemoryCacheOptions()));

    private sealed record Payload(string Value, int Count);

    // Счётчики чтений — статические: открытое поле-счётчик не пропустил бы CacheValueRules. Каждый
    // тип — только в своём тесте, параллельные тесты друг другу их не портят.
    private sealed record Counted(string Value)
    {
        public static int Reads;
        public string Probe { get { Interlocked.Increment(ref Reads); return Value; } }
    }

    private sealed record Sized(string Json) : ICacheSizedValue
    {
        public static int Reads;
        public string Probe { get { Interlocked.Increment(ref Reads); return Json; } }
        long ICacheSizedValue.SizeBytes => 12_345;
    }

    [Fact]
    public async Task Snapshot_DataEntry_SizeIsItsJsonBytes()
    {
        var cache = NewCache();
        var value = new Payload("שלום, мир", 42);
        await cache.GetOrCreateAsync("results:1", () => Task.FromResult(value), Ttl);

        var entry = Assert.Single(cache.Snapshot());

        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(value).LongLength, entry.SizeBytes);
    }

    [Fact]
    public async Task Snapshot_SizedValue_TakesItsOwnSize_WithoutSerializing()
    {
        var cache = NewCache();
        await cache.GetOrCreateAsync("http:records", () => Task.FromResult(new Sized("{}")), Ttl);

        var entry = Assert.Single(cache.Snapshot());

        Assert.Equal(12_345, entry.SizeBytes);
        Assert.Equal(0, Sized.Reads); // готовый ответ не сериализовали
    }

    [Fact]
    public async Task Snapshot_MeasuresEachEntryOnce_NotAtWrite()
    {
        var cache = NewCache();
        await cache.GetOrCreateAsync("swimmer-swims:7", () => Task.FromResult(new Counted("x")), Ttl);
        Assert.Equal(0, Counted.Reads); // запись в кэш размер не меряет — это путь посетителя

        var first = Assert.Single(cache.Snapshot()).SizeBytes;
        var second = Assert.Single(cache.Snapshot()).SizeBytes;

        Assert.Equal(1, Counted.Reads); // второй взгляд админки — из запомненного
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Journal_LastFullReset_IsTheLatestGeneralReset_NotATagReset()
    {
        var cache = NewCache();
        Assert.Null(cache.Journal().LastFullReset);

        await cache.InvalidateAllAsync("импорт протокола #13");
        await cache.SetAsync("k", new Payload("v", 1), Ttl, CacheTags.Table("Results"));
        await cache.InvalidateTagsAsync([CacheTags.Table("Results")], "SaveChanges: Results");

        var last = cache.Journal().LastFullReset;
        Assert.NotNull(last);
        Assert.True(last.All);
        Assert.Equal("импорт протокола #13", last.Reason);

        await cache.InvalidateAllAsync("кнопка «Сбросить весь серверный кэш»");
        Assert.Equal("кнопка «Сбросить весь серверный кэш»", cache.Journal().LastFullReset!.Reason);
    }

    [Fact]
    public void From_SplitsApiAndData_SumsSizes_CountsUntagged()
    {
        var now = DateTimeOffset.UtcNow;
        CacheEntryInfo Entry(string key, long? size, params string[] tags) => new(key, tags, "T", now, now.AddMinutes(5), size);
        var entries = new[]
        {
            Entry("http:clubs:438:overview", 10_000, CacheTags.Table("Clubs"), CacheTags.ClubPages),
            Entry("http:hub-groups:list:private", 2, CacheTags.NotFromDb),
            Entry("swimmer-swims:7", 3_000, CacheTags.Row("Swimmers", 7)),
            Entry("categories:all", null, CacheTags.Table("Categories")), // не измерилась — в сумме нулём
            Entry("broken", 500, CacheTags.ClubPages),                    // одни метки страниц — без меток данных
        };
        var events = Enumerable.Range(0, 7)
            .Select(i => new CacheInvalidationEvent(now.AddMinutes(-i), $"сброс {i}", [CacheTags.Table("Results")], false, 1, []))
            .ToList();
        var fullReset = new CacheInvalidationEvent(now.AddHours(-1), "импорт", [CacheTags.All], true, 40, []);
        var journal = new CacheJournal(now.AddHours(-2), events, 0, [], new CacheHitChecks(0, 0, []), fullReset);

        var o = CacheOverview.From(entries, journal, processBytes: 300_000_000, managedHeapBytes: 90_000_000);

        Assert.Equal((5, 13_502L), (o.Entries, o.Bytes));
        Assert.Equal((2, 10_002L), (o.ApiEntries, o.ApiBytes));
        Assert.Equal((3, 3_500L), (o.DataEntries, o.DataBytes));
        Assert.Equal(1, o.Untagged);
        Assert.Same(fullReset, o.LastFullReset);
        Assert.Equal(journal.Since, o.Since);
        Assert.Equal(["сброс 0", "сброс 1", "сброс 2", "сброс 3", "сброс 4"], o.RecentResets.Select(e => e.Reason));
        Assert.Equal((300_000_000L, 90_000_000L), (o.ProcessBytes, o.ManagedHeapBytes));
    }

    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(1023, "1023 Б")]
    [InlineData(1024, "1 КБ")]
    [InlineData(41 * 1024 + 300, "41 КБ")]
    [InlineData(1024 * 1024, "1,0 МБ")]
    [InlineData(1_887_437, "1,8 МБ")]
    public void Human_BytesAsPeopleReadThem(long bytes, string expected) =>
        Assert.Equal(expected, CacheOverview.Human(bytes));
}
