using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Swimm.Application.Constants;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сторож меток (К5, docs/plans/cache-tags-plan.md §6): собранная запись без меток данных и без
/// объявления <see cref="CacheTags.NotFromDb"/> — ошибка. Главный случай — данные прочитаны ДО
/// <c>GetOrCreateAsync</c> и попали в замыкание (так было у <c>/api/categories/{key}</c>):
/// сохранение в базу такую запись не сбросит, и она врёт до конца срока жизни.
///
/// Касание таблицы здесь — прямой <see cref="CacheBuildScope.Touch"/>, как в
/// <see cref="MemoryCacheServiceTests"/>; настоящий SQL — в <c>CacheDependencyInterceptorPgTests</c>.
/// </summary>
public class CacheUntaggedGuardTests
{
    private sealed record Payload(string Value);

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static MemoryCacheService NewCache(UntaggedEntryPolicy policy, ILogger<MemoryCacheService>? logger = null) =>
        new(new MemoryCache(new MemoryCacheOptions()), logger: logger, untagged: policy);

    public static TheoryData<string> DataTags => new()
    {
        CacheTags.Table("Results"),
        CacheTags.Row("HubGroups", 24),
        CacheTags.AnyRow("HubGroupMembers"),
        CacheTags.Column("Swimmers", "LogligId"),
    };

    public static TheoryData<string> NotDataTags => new()
    {
        CacheTags.All,
        CacheTags.ClubPages,
        CacheTags.ClubPage(438),
        CacheTags.NotFromDb,
    };

    [Theory]
    [MemberData(nameof(DataTags))]
    public void IsData_TablesRowsAndColumns(string tag) => Assert.True(CacheTags.IsData(tag));

    [Theory]
    [MemberData(nameof(NotDataTags))]
    public void IsData_NotPagesOrDeclarations(string tag) => Assert.False(CacheTags.IsData(tag));

    [Fact]
    public async Task Throw_EntryBuiltWithoutDatabase_FailsAndIsNotStored()
    {
        var cache = NewCache(UntaggedEntryPolicy.Throw);
        // Так выглядел /api/categories/{key}: ответ прочитан до кэша, фабрика только отдаёт его.
        var readBefore = new Payload("category");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync("http:categories:open", () => Task.FromResult(readBefore), Ttl));

        Assert.Contains("http:categories:open", error.Message);
        Assert.Contains(nameof(CacheTags.NotFromDb), error.Message);
        Assert.Null(await cache.GetAsync<Payload>("http:categories:open"));
    }

    [Fact]
    public async Task Throw_PageTagsAlone_AreNotEnough()
    {
        // Метки страниц — для кнопок админки, данные они не описывают: запись в базу их не сбросит.
        var cache = NewCache(UntaggedEntryPolicy.Throw);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync("http:clubs:438:overview", () => Task.FromResult(new Payload("c")), Ttl,
                CacheTags.ClubPageTags(438)));
    }

    [Theory]
    [MemberData(nameof(DataTags))]
    public async Task Throw_AnyDataTagFromTheBuild_Passes(string tag)
    {
        var cache = NewCache(UntaggedEntryPolicy.Throw);

        var value = await cache.GetOrCreateAsync("k", () =>
        {
            CacheBuildScope.Current!.Touch(tag);
            return Task.FromResult(new Payload("v"));
        }, Ttl);

        Assert.Equal("v", value.Value);
        Assert.NotNull(await cache.GetAsync<Payload>("k"));
    }

    [Fact]
    public async Task Throw_OuterOnlyHitsNestedEntry_InheritsItsTagsAndPasses()
    {
        // Так починен /api/categories/{key}: внешний ответ SQL не выполняет, а попадает во
        // вложенную запись репозитория — и получает её метки.
        var cache = NewCache(UntaggedEntryPolicy.Throw);
        await cache.GetOrCreateAsync("categories:open", () =>
        {
            CacheBuildScope.Current!.Touch(CacheTags.Table("Categories"));
            return Task.FromResult(new Payload("inner"));
        }, Ttl);

        var outer = await cache.GetOrCreateAsync("http:categories:open",
            () => cache.GetOrCreateAsync("categories:open", () => Task.FromResult(new Payload("never")), Ttl), Ttl);

        Assert.Equal("inner", outer.Value);
        Assert.Contains(cache.Snapshot(), e => e.Key == "http:categories:open" && e.Tags.Contains(CacheTags.Table("Categories")));
    }

    [Fact]
    public async Task Throw_DeclaredNotFromDb_Passes_AndGoesWithTheGeneralReset()
    {
        var cache = NewCache(UntaggedEntryPolicy.Throw);

        var value = await cache.GetOrCreateAsync("http:hub-groups:list:private",
            () => Task.FromResult(new Payload("[]")), Ttl, CacheTags.NotFromDb);

        Assert.Equal("[]", value.Value);
        await cache.InvalidateAllAsync();
        Assert.Null(await cache.GetAsync<Payload>("http:hub-groups:list:private"));
    }

    [Fact]
    public async Task Throw_NotFound_IsNotChecked()
    {
        // «Не найдено» (null) в кэш не ложится — сторожить нечего.
        var cache = NewCache(UntaggedEntryPolicy.Throw);

        var value = await cache.GetOrCreateAsync("swimmer-profile:0", () => Task.FromResult<Payload?>(null), Ttl);

        Assert.Null(value);
    }

    [Fact]
    public async Task Warn_StoresTheEntry_AndWarnsOncePerKind()
    {
        var log = new CapturingLogger();
        var cache = NewCache(UntaggedEntryPolicy.Warn, log);

        await cache.GetOrCreateAsync("http:categories:open", () => Task.FromResult(new Payload("a")), Ttl);
        await cache.GetOrCreateAsync("http:categories:masters", () => Task.FromResult(new Payload("b")), Ttl);
        await cache.GetOrCreateAsync("http:filter-hints:name::20", () => Task.FromResult(new Payload("c")), Ttl);

        // Прод не ломаем: запись легла, как до К5.
        Assert.NotNull(await cache.GetAsync<Payload>("http:categories:open"));
        // Вид http:categories — одно предупреждение на две записи.
        Assert.Equal(2, log.Warnings.Count);
        Assert.Contains("http:categories:open", log.Warnings[0]);
        Assert.Contains("http:filter-hints:name::20", log.Warnings[1]);
    }

    [Fact]
    public async Task Allow_IsTheDefault_AsBeforeK5()
    {
        // Кэш из тестов собирается без режима — фабрики там сплошь без базы.
        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));

        var value = await cache.GetOrCreateAsync("k", () => Task.FromResult(new Payload("v")), Ttl);

        Assert.Equal("v", value.Value);
    }

    private sealed class CapturingLogger : ILogger<MemoryCacheService>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
