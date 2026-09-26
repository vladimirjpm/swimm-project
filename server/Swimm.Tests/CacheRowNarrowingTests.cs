using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Настройки кэша для тестов: выключатели сужения до строк и точности по колонкам, доля сверки —
/// как на /Admin/Settings. Выключатели по умолчанию — как дефолты на сайте.
/// </summary>
internal sealed class CacheSettingsStub(
    bool rowPrecision = CacheSettings.DefaultRowPrecision,
    int hitVerifyPercent = 0,
    bool columnPrecision = CacheSettings.DefaultColumnPrecision)
    : ISettingsService
{
    private readonly Dictionary<string, string> _values = new()
    {
        [CacheSettings.RowPrecision] = rowPrecision ? "true" : "false",
        [CacheSettings.HitVerifyPercent] = hitVerifyPercent.ToString(),
        [CacheSettings.ColumnPrecision] = columnPrecision ? "true" : "false",
    };

    public IReadOnlyList<AdminSetting> GetAll() => [];
    public AdminSetting? Get(string key) => null;

    public T GetValue<T>(string key, T fallback) =>
        _values.TryGetValue(key, out var v) ? (T)Convert.ChangeType(v, typeof(T)) : fallback;

    public bool Update(string key, string newValue)
    {
        _values[key] = newValue;
        return true;
    }
}

/// <summary>
/// Сужение при чтении (docs/plans/cache-row-precision-plan.md §2.3, К4б.3): в блоке
/// <c>CacheRows</c> чтение перечисленных таблиц даёт записи кэша метки строк корня и
/// <c>anyrow:T</c> вместо <c>table:T</c> — страница группы 24 падает от правки своих строк и живёт
/// при правке чужих.
///
/// Логика — на <see cref="MemoryCacheService"/> без базы: касание таблицы здесь делает
/// <see cref="CacheBuildScope.TouchTable"/> напрямую — так поступает перехватчик SQL. Проверка
/// блока по модели — на настоящей модели в InMemory; настоящий SQL — на живом Postgres, только
/// чтение (нет базы — пропуск).
/// </summary>
public class CacheRowNarrowingTests
{
    private const string PgConn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private sealed record Payload(string Value);

    private static readonly IReadOnlySet<string> GroupTables =
        new HashSet<string>(StringComparer.Ordinal) { "HubGroups", "HubGroupMembers" };

    private static MemoryCacheService Cache(bool rowPrecision = true) =>
        new(new MemoryCache(new MemoryCacheOptions()), new CacheSettingsStub(rowPrecision));

    private static IReadOnlyList<string> TagsOf(MemoryCacheService cache, string key) =>
        cache.Snapshot().Single(e => e.Key == key).Tags;

    private static async Task<bool> Cached(MemoryCacheService cache, string key) =>
        await cache.GetAsync<Payload>(key) is not null;

    /// <summary>Страница группы: участники читаются в блоке группы, стили — таблицей.</summary>
    private static Task<Payload> GroupPage(MemoryCacheService cache, IReadOnlyCollection<long> ids, string key = "group-24") =>
        cache.GetOrCreateAsync(key, () =>
        {
            var scope = CacheBuildScope.Current!;
            using (scope.Narrow("HubGroups", ids, GroupTables))
            {
                scope.TouchTable("HubGroupMembers"); // так перехватчик отмечает SQL участников
                scope.TouchTable("Styles");
            }
            return Task.FromResult(new Payload("g"));
        }, Ttl);

    // ── Что даёт блок ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NarrowedRead_DependsOnItsRows_NotOnTheTable()
    {
        var cache = Cache();
        await GroupPage(cache, [24]);

        var tags = TagsOf(cache, "group-24");
        Assert.Contains(CacheTags.Row("HubGroups", 24), tags);
        Assert.Contains(CacheTags.AnyRow("HubGroupMembers"), tags);
        Assert.DoesNotContain(CacheTags.Table("HubGroupMembers"), tags);
        Assert.Contains(CacheTags.Table("Styles"), tags); // таблица вне блока — как раньше
    }

    [Fact]
    public async Task EditOfAnotherGroup_KeepsThePage_EditOfItsOwn_DropsIt()
    {
        var cache = Cache();
        await GroupPage(cache, [24]);

        // Так сбрасывает правка участника группы 17 (К4б.2): таблица и строка чужого корня.
        await cache.InvalidateTagsAsync(CacheTags.Table("HubGroupMembers"), CacheTags.Row("HubGroups", 17));
        Assert.True(await Cached(cache, "group-24"));

        await cache.InvalidateTagsAsync(CacheTags.Table("HubGroupMembers"), CacheTags.Row("HubGroups", 24));
        Assert.False(await Cached(cache, "group-24"));
    }

    [Fact]
    public async Task RowsUnknown_AnyRow_DropsThePage()
    {
        var cache = Cache();
        await GroupPage(cache, [24]);

        // Массовая запись, каскад, сжатие: какие строки задеты — неизвестно.
        await cache.InvalidateTagsAsync(CacheTags.Table("HubGroupMembers"), CacheTags.AnyRow("HubGroupMembers"));

        Assert.False(await Cached(cache, "group-24"));
    }

    [Fact]
    public async Task NestedBuild_DoesNotInheritTheNarrowing()
    {
        var cache = Cache();
        await cache.GetOrCreateAsync("group-24", async () =>
        {
            var scope = CacheBuildScope.Current!;
            using (scope.Narrow("HubGroups", [24], GroupTables))
            {
                // Общая запись, собранная внутри блока группы 24, — все участники всех групп.
                await cache.GetOrCreateAsync("all-members", () =>
                {
                    CacheBuildScope.Current!.TouchTable("HubGroupMembers");
                    return Task.FromResult(new Payload("all"));
                }, Ttl);
            }
            return new Payload("g");
        }, Ttl);

        // С меткой группы 24 она потом врала бы всем: правка группы 17 её бы не сбросила.
        var nested = TagsOf(cache, "all-members");
        Assert.Contains(CacheTags.Table("HubGroupMembers"), nested);
        Assert.DoesNotContain(CacheTags.Row("HubGroups", 24), nested);
        Assert.DoesNotContain(CacheTags.AnyRow("HubGroupMembers"), nested);
        // Внешняя запись собрана из табличной — и зависит от таблицы, как положено.
        Assert.Contains(CacheTags.Table("HubGroupMembers"), TagsOf(cache, "group-24"));
    }

    [Fact]
    public async Task SwitchOff_TheBlockDoesNothing()
    {
        var cache = Cache(rowPrecision: false);
        await GroupPage(cache, [24]);

        var tags = TagsOf(cache, "group-24");
        Assert.Contains(CacheTags.Table("HubGroupMembers"), tags);
        Assert.DoesNotContain(CacheTags.Row("HubGroups", 24), tags);
    }

    [Fact]
    public async Task OverTheCeiling_TheBlockWorksAsTable()
    {
        var max = CacheBuildScope.MaxNarrowedIds;
        var cache = Cache();

        await GroupPage(cache, Enumerable.Range(1, max).Select(i => (long)i).ToArray(), "at-max");
        await GroupPage(cache, Enumerable.Range(1, max + 1).Select(i => (long)i).ToArray(), "over-max");

        Assert.Contains(CacheTags.Row("HubGroups", max), TagsOf(cache, "at-max"));
        Assert.DoesNotContain(CacheTags.Table("HubGroupMembers"), TagsOf(cache, "at-max"));
        Assert.Contains(CacheTags.Table("HubGroupMembers"), TagsOf(cache, "over-max"));
        Assert.DoesNotContain(TagsOf(cache, "over-max"), CacheTags.IsRowLevel);
    }

    [Fact]
    public async Task EmptyIds_DependOnlyOnAnyRow()
    {
        var cache = Cache();
        await GroupPage(cache, []);

        // FK IN () ничего не читает: от строк зависеть нечему, остаётся «неизвестно какие».
        var tags = TagsOf(cache, "group-24");
        Assert.Contains(CacheTags.AnyRow("HubGroupMembers"), tags);
        Assert.DoesNotContain(tags, t => t.StartsWith("row:", StringComparison.Ordinal));
        Assert.DoesNotContain(CacheTags.Table("HubGroupMembers"), tags);
    }

    [Fact]
    public async Task AfterTheBlock_ReadsDependOnTheTableAgain()
    {
        var cache = Cache();
        await cache.GetOrCreateAsync("group-24", () =>
        {
            var scope = CacheBuildScope.Current!;
            using (scope.Narrow("HubGroups", [24], GroupTables)) scope.TouchTable("HubGroupMembers");
            scope.TouchTable("HubGroupMembers"); // вне блока — чужие строки могли прийти
            return Task.FromResult(new Payload("g"));
        }, Ttl);

        Assert.Contains(CacheTags.Table("HubGroupMembers"), TagsOf(cache, "group-24"));
    }

    [Fact]
    public async Task InnerBlock_ReplacesTheOuter_AndTheOuterComesBack()
    {
        var cache = Cache();
        await cache.GetOrCreateAsync("group-24", () =>
        {
            var scope = CacheBuildScope.Current!;
            using (scope.Narrow("HubGroups", [24], new HashSet<string> { "HubGroupMembers" }))
            {
                // Официальная группа клуба 438 — строки HubGroups клуба (Q3b плана).
                using (scope.Narrow("Clubs", [438], new HashSet<string> { "HubGroups" }))
                {
                    scope.TouchTable("HubGroups");
                    scope.TouchTable("HubGroupMembers"); // внутренний блок участников не сужает
                }
                scope.TouchTable("HubGroupMembers");     // внешний вернулся
            }
            return Task.FromResult(new Payload("g"));
        }, Ttl);

        Assert.Equal(
            new[]
            {
                CacheTags.AnyRow("HubGroupMembers"), CacheTags.AnyRow("HubGroups"),
                CacheTags.Row("Clubs", 438), CacheTags.Row("HubGroups", 24), CacheTags.Table("HubGroupMembers"),
            }.Order(StringComparer.Ordinal),
            TagsOf(cache, "group-24"));
    }

    // ── CacheRows: проверка по модели ────────────────────────────────────────────────

    private static SwimmDbContext Model(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    [Fact]
    public void CacheRows_RootOutsideTheRegistry_Throws()
    {
        using var db = Model(nameof(CacheRows_RootOutsideTheRegistry_Throws));

        // Метку row:Competitions:… запись в базу не сбрасывает — сужение по ней врало бы вечно.
        Assert.Throws<InvalidOperationException>(() => db.CacheRows<Competition>(1));
    }

    [Fact]
    public void CacheRows_TableWithoutForeignKeyToTheRoot_Throws()
    {
        using var db = Model(nameof(CacheRows_TableWithoutForeignKeyToTheRoot_Throws));

        Assert.Throws<InvalidOperationException>(() => db.CacheRows<HubGroup>(24, typeof(Competition)));
    }

    [Fact]
    public void CacheRows_ChildOfAnOwnRowOnlyRoot_Throws()
    {
        using var db = Model(nameof(CacheRows_ChildOfAnOwnRowOnlyRoot_Throws));

        // FK на пользователя меток не дают — сужать под ним избранное значило бы недосброс.
        Assert.Throws<InvalidOperationException>(() => db.CacheRows<AppUser>(1, typeof(UserFavorite)));
        db.CacheRows<AppUser>([1, 2]).Dispose(); // только свои строки — можно
    }

    [Fact]
    public void CacheRows_Grandchild_Throws()
    {
        using var db = Model(nameof(CacheRows_Grandchild_Throws));

        // Результат тренировки ссылается на тренировку, та — на группу: row:HubGroups:24 правка
        // результата не сбрасывает (потомок — только прямой).
        Assert.Throws<InvalidOperationException>(() => db.CacheRows<HubGroup>(24, typeof(TrainingResult)));
    }

    [Fact]
    public async Task CacheRows_InsideABuild_NarrowsTheModelTables()
    {
        using var db = Model(nameof(CacheRows_InsideABuild_NarrowsTheModelTables));
        var cache = Cache();

        await cache.GetOrCreateAsync("group-24", () =>
        {
            using (db.CacheRows<HubGroup>(24, typeof(HubGroupMember), typeof(HubGroupMedia)))
            {
                CacheBuildScope.Current!.TouchTable("HubGroupMembers");
                CacheBuildScope.Current!.TouchTable("Sys_HubGroupMedia"); // имя таблицы — из модели
                CacheBuildScope.Current!.TouchTable("HubGroups");         // сам корень сужен тоже
            }
            return Task.FromResult(new Payload("g"));
        }, Ttl);

        Assert.Equal(
            new[]
            {
                CacheTags.AnyRow("HubGroupMembers"), CacheTags.AnyRow("HubGroups"),
                CacheTags.AnyRow("Sys_HubGroupMedia"), CacheTags.Row("HubGroups", 24),
            }.Order(StringComparer.Ordinal),
            TagsOf(cache, "group-24"));
    }

    [Fact]
    public void CacheRows_OutsideABuild_IsEmpty_ButStillChecked()
    {
        using var db = Model(nameof(CacheRows_OutsideABuild_IsEmpty_ButStillChecked));
        Assert.Null(CacheBuildScope.Current);

        // Некэшированный путь зовёт тот же метод — блок пустой, но проверка по модели та же.
        using (db.CacheRows<HubGroup>(24, typeof(HubGroupMember))) { }
        Assert.Throws<InvalidOperationException>(() => db.CacheRows<HubGroup>(24, typeof(Competition)));
    }

    // ── Настоящий SQL (живой Postgres, только чтение) ──────────────────────────────────

    [Fact]
    public async Task RealSql_InTheBlock_NarrowsTheMembers_TheJoinedSwimmersStayTableLevel()
    {
        var interceptor = new CacheDependencyInterceptor();
        await using var db = new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseNpgsql(PgConn)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(interceptor)
            .Options);
        try { if (!await db.Database.CanConnectAsync()) return; } catch { return; } // нет базы — пропуск
        var cache = Cache();

        // Q2 плана: участники группы (сужено) с пловцами (таблицей — их читают и чужие запросы).
        await cache.GetOrCreateAsync("group-24", async () =>
        {
            using (db.CacheRows<HubGroup>(24, typeof(HubGroupMember)))
            {
                var members = await db.HubGroupMembers
                    .Where(m => m.HubGroupId == 24)
                    .Select(m => new { m.Id, m.Swimmer!.LastName })
                    .ToListAsync();
                return new Payload(members.Count.ToString());
            }
        }, Ttl);

        var tags = TagsOf(cache, "group-24");
        Assert.Contains(CacheTags.Row("HubGroups", 24), tags);
        Assert.Contains(CacheTags.AnyRow("HubGroupMembers"), tags);
        Assert.DoesNotContain(CacheTags.Table("HubGroupMembers"), tags);
        Assert.Contains(CacheTags.Table("Swimmers"), tags);
    }

    // ── Подметание источников меток (§7) ────────────────────────────────────────────

    /// <summary>Сборка, которая касается метки и ничего не кладёт: «не найдено» в кэш не ложится.</summary>
    private static Task TouchOnly(MemoryCacheService cache, string key, params string[] tags) =>
        cache.GetOrCreateAsync<Payload?>(key, () =>
        {
            foreach (var tag in tags) CacheBuildScope.Current!.Touch(tag);
            return Task.FromResult<Payload?>(null);
        }, Ttl);

    [Fact]
    public async Task Sweep_DropsSourcesNoEntryWears_KeepsTheWornOnes()
    {
        var cache = Cache();
        await cache.GetOrCreateAsync("live", () =>
        {
            CacheBuildScope.Current!.Touch(CacheTags.Table("Kept"));
            return Task.FromResult(new Payload("l"));
        }, Ttl);
        for (var i = 0; i < 50; i++) await TouchOnly(cache, $"gone-{i}", CacheTags.Row("Swimmers", i));
        Assert.True(cache.TagSourceCount >= 51);

        cache.SweepTags();

        Assert.Equal(1, cache.TagSourceCount);
        Assert.True(await Cached(cache, "live"));
        await cache.InvalidateTagsAsync(CacheTags.Table("Kept")); // источник живой записи цел
        Assert.False(await Cached(cache, "live"));
    }

    [Fact]
    public async Task Sweep_CancelsTheSource_SoABuildInFlightIsNotStored()
    {
        var cache = Cache();

        await cache.GetOrCreateAsync("in-flight", () =>
        {
            CacheBuildScope.Current!.Touch(CacheTags.Row("Swimmers", 5));
            // Запись ещё не легла — метку не носит никто, подметание её выбросит. Без отмены
            // сборка легла бы с токеном, до которого сброс row:Swimmers:5 уже не дотянется.
            cache.SweepTags();
            return Task.FromResult(new Payload("p"));
        }, Ttl);

        Assert.False(await Cached(cache, "in-flight"));
    }

    [Fact]
    public async Task Sweep_RunsByItself_OverTheThreshold()
    {
        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions())) { TagSweepThreshold = 100 };

        for (var i = 0; i < 4096; i++) await TouchOnly(cache, $"k-{i}", CacheTags.Row("Swimmers", i));

        // Проверка раз в 1024 касания: словарь не держит больше порога плюс одного окна.
        Assert.True(cache.TagSourceCount <= 100 + 1024, $"источников {cache.TagSourceCount}");
    }
}
