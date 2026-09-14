using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Метки кэша из настоящего SQL (docs/plans/cache-tags-plan.md, К3): перехватчик команд EF
/// отмечает в сборке таблицы, которых коснулся запрос. InMemory SQL не выполняет — поэтому
/// против живого Postgres; пропуск, если он недоступен. Тест только ЧИТАЕТ базу.
///
/// Приёмка К3: сброс метки групп (так поступит правка расписания в К4) убирает страницу
/// группы и НЕ трогает season-best — сегодня любая запись сбрасывает всё.
/// </summary>
public class CacheDependencyInterceptorPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static readonly CacheDependencyInterceptor Interceptor = new();

    private static SwimmReadDbContext? TryRead()
    {
        var db = new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseNpgsql(Conn)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(Interceptor)
            .Options);
        try { if (db.Database.CanConnect()) return db; } catch { /* нет базы */ }
        db.Dispose();
        return null;
    }

    private static SwimmDbContext Rw() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).AddInterceptors(Interceptor).Options);

    private static MemoryCacheService NewCache() => new(new MemoryCache(new MemoryCacheOptions()));

    private sealed class SettingsStub : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    private static IReadOnlyList<string> TagsOf(MemoryCacheService cache, string key) =>
        cache.Snapshot().Single(e => e.Key == key).Tags;

    [Fact]
    public async Task Query_InsideBuild_TagsExactlyTheTablesItRead()
    {
        await using var db = TryRead();
        if (db == null) return; // PG недоступен — пропуск
        var cache = NewCache();

        await cache.GetOrCreateAsync("latest-results", () => db.Results
            .OrderByDescending(r => r.Id)
            .Select(r => new { r.Id, Club = r.Club.Name })
            .Take(3)
            .ToListAsync(), TimeSpan.FromMinutes(1));

        var tags = TagsOf(cache, "latest-results");
        Assert.Contains(CacheTags.Table("Results"), tags);
        Assert.Contains(CacheTags.Table("Clubs"), tags);        // JOIN тоже зависимость
        Assert.DoesNotContain(CacheTags.Table("HubGroups"), tags);
    }

    [Fact]
    public async Task Query_OutsideBuild_TagsNothing()
    {
        await using var db = TryRead();
        if (db == null) return;

        // Вне сборки перехватчику некуда писать — и он молчит, а не падает.
        Assert.Null(CacheBuildScope.Current);
        await db.Results.Select(r => r.Id).Take(1).ToListAsync();
    }

    [Fact]
    public async Task GroupsTagInvalidated_DropsGroupPage_KeepsSeasonBest()
    {
        await using var read = TryRead();
        if (read == null) return;
        var slug = await read.HubGroups.OrderBy(g => g.Id).Select(g => g.Slug).FirstOrDefaultAsync();
        if (slug == null) return; // групп нет — нечего проверять
        await using var rw = Rw();
        var cache = NewCache();

        var seasonBest = new SeasonBestRepository(read, new ShowcaseSeasonProvider(read, cache));
        var groups = new HubGroupPublicRepository(read, rw, new SettingsStub());

        await cache.GetOrCreateAsync("http:season-best:table:cur",
            () => seasonBest.GetSeasonBestTableAsync(null), TimeSpan.FromMinutes(5));
        await cache.GetOrCreateAsync<HubGroupDetailsDto?>($"http:hub-groups:group:{slug}",
            () => groups.GetBySlugAsync(slug), TimeSpan.FromMinutes(5));

        var sbTags = TagsOf(cache, "http:season-best:table:cur");
        Assert.Contains(CacheTags.Table("Results"), sbTags);
        // Витринный сезон — вложенная запись; её метка перешла в season-best.
        Assert.Contains(CacheTags.Table("Competitions"), sbTags);
        Assert.DoesNotContain(CacheTags.Table("HubGroups"), sbTags);
        Assert.Contains(CacheTags.Table("HubGroups"), TagsOf(cache, $"http:hub-groups:group:{slug}"));

        await cache.InvalidateTagsAsync(CacheTags.Table("HubGroups"));

        Assert.DoesNotContain(cache.Snapshot(), e => e.Key == $"http:hub-groups:group:{slug}");
        Assert.Contains(cache.Snapshot(), e => e.Key == "http:season-best:table:cur");
    }
}
