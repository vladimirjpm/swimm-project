using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Constants;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Кэш с метками (docs/plans/cache-tags-plan.md, этап К1): сброс по метке роняет только свои
/// записи, общий сброс — все, параллельные промахи одного ключа строят ответ ОДИН раз, а
/// собранное во время сброса в кэш не ложится.
///
/// Эти же тесты обязан пройти будущий RedisCacheService — поведение потребителям не меняется.
///
/// К3: метки ставятся сами — касание таблицы в сборке (так делает перехватчик SQL, здесь его
/// роль играет прямой <see cref="CacheBuildScope.Touch"/>) и наследование от вложенных записей.
/// Настоящий SQL — в <c>CacheDependencyInterceptorPgTests</c>.
/// </summary>
public class MemoryCacheServiceTests
{
    private static MemoryCacheService NewCache() => new(new MemoryCache(new MemoryCacheOptions()));

    private sealed record Payload(string Value);

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task InvalidateTag_DropsOnlyEntriesWithThatTag()
    {
        var cache = NewCache();
        var records = CacheTags.Table("Records");
        var group24 = CacheTags.Row("HubGroups", 24);

        await cache.SetAsync("records-page", new Payload("r"), Ttl, records);
        await cache.SetAsync("group-24", new Payload("g"), Ttl, group24);
        await cache.SetAsync("untagged", new Payload("u"), Ttl);

        await cache.InvalidateTagsAsync(group24);

        Assert.NotNull(await cache.GetAsync<Payload>("records-page"));
        Assert.Null(await cache.GetAsync<Payload>("group-24"));
        Assert.NotNull(await cache.GetAsync<Payload>("untagged"));
    }

    [Fact]
    public async Task Entry_WithSeveralTags_DropsOnAnyOfThem()
    {
        var cache = NewCache();
        await cache.SetAsync("club-page", new Payload("c"), Ttl,
            CacheTags.Table("Records"), CacheTags.Row("Clubs", 438));

        await cache.InvalidateTagsAsync(CacheTags.Table("Records"));

        Assert.Null(await cache.GetAsync<Payload>("club-page"));
    }

    [Fact]
    public async Task InvalidateAll_DropsTaggedAndUntagged_AndTagStillWorksAfter()
    {
        var cache = NewCache();
        var tag = CacheTags.Table("Results");
        await cache.SetAsync("a", new Payload("a"), Ttl, tag);
        await cache.SetAsync("b", new Payload("b"), Ttl);

        await cache.InvalidateAllAsync();

        Assert.Null(await cache.GetAsync<Payload>("a"));
        Assert.Null(await cache.GetAsync<Payload>("b"));

        // После общего сброса метки не «сломаны»: новая запись снова сбрасывается своей меткой.
        await cache.SetAsync("c", new Payload("c"), Ttl, tag);
        await cache.InvalidateTagsAsync(tag);
        Assert.Null(await cache.GetAsync<Payload>("c"));
    }

    [Fact]
    public async Task InvalidateTags_WithAll_IsTheSameAsInvalidateAll()
    {
        var cache = NewCache();
        await cache.SetAsync("a", new Payload("a"), Ttl);

        await cache.InvalidateTagsAsync(CacheTags.All);

        Assert.Null(await cache.GetAsync<Payload>("a"));
    }

    [Fact]
    public async Task GetOrCreate_ParallelMisses_BuildOnce()
    {
        var cache = NewCache();
        var builds = 0;
        var gate = new TaskCompletionSource();

        async Task<Payload> Build()
        {
            Interlocked.Increment(ref builds);
            await gate.Task; // держим сборку, пока все запросы не встанут в очередь
            return new Payload("built");
        }

        var callers = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => cache.GetOrCreateAsync("season-best", Build, Ttl)))
            .ToArray();
        await Task.Delay(100);
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, builds);
        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.NotNull(await cache.GetAsync<Payload>("season-best"));
    }

    [Fact]
    public async Task GetOrCreate_InvalidatedDuringBuild_ReturnsValueButDoesNotCacheIt()
    {
        var cache = NewCache();
        var tag = CacheTags.Row("HubGroups", 24);
        var gate = new TaskCompletionSource();

        var pending = cache.GetOrCreateAsync("group-24", async () =>
        {
            await gate.Task;
            return new Payload("built-from-old-data");
        }, Ttl, tag);

        // Правка в админке пришла, пока тяжёлый ответ строился из СТАРЫХ данных.
        await cache.InvalidateTagsAsync(tag);
        gate.SetResult();

        Assert.Equal("built-from-old-data", (await pending).Value);
        Assert.Null(await cache.GetAsync<Payload>("group-24"));
    }

    [Fact]
    public async Task GetOrCreate_FailedBuild_IsRetriedByNextCall()
    {
        var cache = NewCache();
        var attempts = 0;

        Task<Payload> Build()
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<Payload>(new InvalidOperationException("db down"))
                : Task.FromResult(new Payload("ok"));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync("k", Build, Ttl));
        var second = await cache.GetOrCreateAsync("k", Build, Ttl);

        Assert.Equal("ok", second.Value);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task GetOrCreate_Hit_DoesNotBuild()
    {
        var cache = NewCache();
        await cache.SetAsync("k", new Payload("cached"), Ttl);

        var value = await cache.GetOrCreateAsync("k",
            () => Task.FromResult(new Payload("rebuilt")), Ttl);

        Assert.Equal("cached", value.Value);
    }

    // ── К3: метки ставятся сами — из сборки и вложенных записей ─────────────────

    [Fact]
    public async Task Touch_DuringBuild_TagsTheEntry()
    {
        // Так метку ставит перехватчик SQL: касание таблицы внутри сборки.
        var cache = NewCache();
        await cache.GetOrCreateAsync("season-best", () =>
        {
            CacheBuildScope.Current!.Touch(CacheTags.Table("Results"));
            return Task.FromResult(new Payload("sb"));
        }, Ttl);

        await cache.InvalidateTagsAsync(CacheTags.Table("HubGroups"));
        Assert.NotNull(await cache.GetAsync<Payload>("season-best"));

        await cache.InvalidateTagsAsync(CacheTags.Table("Results"));
        Assert.Null(await cache.GetAsync<Payload>("season-best"));
    }

    [Fact]
    public async Task Touch_ThenTableInvalidatedBeforeBuildEnds_NotCached()
    {
        // Таблицу прочитали, а до конца сборки её поменяли: собранное устарело.
        var cache = NewCache();
        var tag = CacheTags.Table("Records");

        await cache.GetOrCreateAsync("club-page", async () =>
        {
            CacheBuildScope.Current!.Touch(tag);
            await cache.InvalidateTagsAsync(tag);
            return new Payload("stale");
        }, Ttl);

        Assert.Null(await cache.GetAsync<Payload>("club-page"));
    }

    [Fact]
    public async Task Nested_InnerMiss_OuterInheritsInnerTags()
    {
        var cache = NewCache();
        var competitions = CacheTags.Table("Competitions");

        await cache.GetOrCreateAsync("season-best", async () =>
        {
            // Витринный сезон — своя запись кэша, собранная внутри season-best.
            var season = await cache.GetOrCreateAsync("showcase-season", () =>
            {
                CacheBuildScope.Current!.Touch(competitions);
                return Task.FromResult(new Payload("2025/26"));
            }, Ttl);
            return new Payload("sb for " + season.Value);
        }, Ttl);

        await cache.InvalidateTagsAsync(competitions);

        Assert.Null(await cache.GetAsync<Payload>("showcase-season"));
        Assert.Null(await cache.GetAsync<Payload>("season-best"));
    }

    [Fact]
    public async Task Nested_InnerHit_OuterStillInheritsInnerTags()
    {
        // Попадание во вложенный кэш: SQL не выполнялся, и без наследования внешний ответ не
        // узнал бы, из чего собран внутренний.
        var cache = NewCache();
        var records = CacheTags.Table("Records");
        await cache.SetAsync("israel-records", new Payload("axes"), Ttl, records);

        await cache.GetOrCreateAsync("overview", async () =>
        {
            var axes = await cache.GetAsync<Payload>("israel-records");
            return new Payload("overview with " + axes!.Value);
        }, Ttl);

        await cache.InvalidateTagsAsync(records);

        Assert.Null(await cache.GetAsync<Payload>("overview"));
    }

    [Fact]
    public async Task Waiters_OnSharedInnerBuild_AllInheritItsTags()
    {
        // Два внешних ответа ждут одну вложенную сборку — наследовать должны оба, а не только
        // тот, чей запрос её запустил.
        var cache = NewCache();
        var tag = CacheTags.Table("Competitions");
        var gate = new TaskCompletionSource();

        async Task<Payload> Inner()
        {
            CacheBuildScope.Current!.Touch(tag);
            await gate.Task;
            return new Payload("inner");
        }

        var first = cache.GetOrCreateAsync("outer-1", async () =>
            new Payload((await cache.GetOrCreateAsync("inner", Inner, Ttl)).Value), Ttl);
        var second = cache.GetOrCreateAsync("outer-2", async () =>
            new Payload((await cache.GetOrCreateAsync("inner", Inner, Ttl)).Value), Ttl);
        gate.SetResult();
        await Task.WhenAll(first, second);

        await cache.InvalidateTagsAsync(tag);

        Assert.Null(await cache.GetAsync<Payload>("outer-1"));
        Assert.Null(await cache.GetAsync<Payload>("outer-2"));
    }

    [Fact]
    public async Task ManualSet_InsideBuild_TakesTagsGatheredSoFar()
    {
        var cache = NewCache();
        var tag = CacheTags.Table("Styles");

        await cache.GetOrCreateAsync("page", async () =>
        {
            CacheBuildScope.Current!.Touch(tag);
            await cache.SetAsync("manual", new Payload("m"), Ttl);
            return new Payload("p");
        }, Ttl);

        await cache.InvalidateTagsAsync(tag);

        Assert.Null(await cache.GetAsync<Payload>("manual"));
    }

    [Fact]
    public async Task OwnersCancelledBuild_DoesNotFailWaiters()
    {
        // Фабрики получают токен запроса. Оборвали запрос того, кто строит, — ждавший не должен
        // упасть вместе с ним, а строит сам.
        var cache = NewCache();
        var gate = new TaskCompletionSource();

        var owner = cache.GetOrCreateAsync<Payload>("sb", async () =>
        {
            await gate.Task;
            throw new OperationCanceledException("request aborted");
        }, Ttl);
        var waiter = cache.GetOrCreateAsync("sb", () => Task.FromResult(new Payload("mine")), Ttl);
        gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner);
        Assert.Equal("mine", (await waiter).Value);
    }

    [Fact]
    public async Task NullFromFactory_IsNotCached()
    {
        var cache = NewCache();
        var calls = 0;

        Task<Payload?> NotFound() { calls++; return Task.FromResult<Payload?>(null); }

        Assert.Null(await cache.GetOrCreateAsync("category:nope", NotFound, Ttl));
        Assert.Null(await cache.GetOrCreateAsync("category:nope", NotFound, Ttl));
        Assert.Equal(2, calls);
        Assert.DoesNotContain(cache.Snapshot(), e => e.Key == "category:nope");
    }

    [Fact]
    public async Task Snapshot_ListsLiveEntriesWithTheirTags()
    {
        var cache = NewCache();
        await cache.GetOrCreateAsync("club-page", () =>
        {
            CacheBuildScope.Current!.Touch(CacheTags.Table("Records"));
            CacheBuildScope.Current!.Touch(CacheTags.Table("Clubs"));
            return Task.FromResult(new Payload("c"));
        }, Ttl);

        var entry = Assert.Single(cache.Snapshot());
        Assert.Equal("club-page", entry.Key);
        Assert.Equal(["table:Clubs", "table:Records"], entry.Tags); // без неявной all, по алфавиту

        await cache.InvalidateTagsAsync(CacheTags.Table("Clubs"));
        Assert.Empty(cache.Snapshot());
    }

    [Fact]
    public async Task Set_TupleValue_IsRefused()
    {
        // Память проглотила бы кортеж, а Redis записал бы его как {} — ловим уже сейчас.
        var cache = NewCache();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.SetAsync("page", (new List<int> { 1 }, true, 3), Ttl));
        Assert.Contains("кортеж", ex.Message);
    }
}
