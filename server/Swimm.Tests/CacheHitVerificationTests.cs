using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Constants;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сверка на попадании (docs/plans/cache-row-precision-plan.md §4-6, К4б.3): попадание в запись,
/// суженную до строк, с долей из настроек строится заново мимо кэша и сравнивается. Не совпало —
/// «подозрение на недосброс» в журнал, запись вон, ответ — свежий. Ловит нарушение правила
/// сужения, которого не предусмотрели тесты.
///
/// «База» здесь — словарь: страница группы 24 читает из него строки. Метки ставит прямое
/// касание таблицы в блоке, как перехватчик SQL.
/// </summary>
public class CacheHitVerificationTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private sealed record Page(string Members);

    private static readonly IReadOnlySet<string> MembersTable =
        new HashSet<string>(StringComparer.Ordinal) { "HubGroupMembers" };

    private static MemoryCacheService Cache(int percent = 100) =>
        new(new MemoryCache(new MemoryCacheOptions()), new CacheSettingsStub(rowPrecision: true, hitVerifyPercent: percent));

    /// <summary>Участники по группам — «строки HubGroupMembers».</summary>
    private readonly Dictionary<int, string> _members = new() { [24] = "ann", [17] = "bob" };

    private int _builds;

    /// <summary>
    /// Страница группы 24, суженная по группе 24. <paramref name="broken"/> — нарушение правила
    /// §2.3: блок заявлен по группе 24, а читает и строку группы 17.
    /// </summary>
    private Task<Page> GroupPage(MemoryCacheService cache, bool broken = false) =>
        cache.GetOrCreateAsync("group-24", () =>
        {
            _builds++;
            var scope = CacheBuildScope.Current!;
            using (scope.Narrow("HubGroups", [24], MembersTable))
            {
                scope.TouchTable("HubGroupMembers");
                return Task.FromResult(new Page(broken ? $"{_members[24]}+{_members[17]}" : _members[24]));
            }
        }, Ttl);

    /// <summary>Правка участника группы 17 — метки, которые сбросит перехватчик сохранения (К4б.2).</summary>
    private async Task EditGroup17(MemoryCacheService cache, string members)
    {
        _members[17] = members;
        await cache.InvalidateTagsAsync(CacheTags.Table("HubGroupMembers"), CacheTags.Row("HubGroups", 17));
    }

    [Fact]
    public async Task BrokenNarrowing_IsCaughtOnHit_AndTheFreshAnswerIsServed()
    {
        var cache = Cache();
        await GroupPage(cache, broken: true);
        await EditGroup17(cache, "bill");
        Assert.NotNull(await cache.GetAsync<Page>("group-24")); // сломанное сужение пережило чужую правку

        var page = await GroupPage(cache, broken: true);

        Assert.Equal("ann+bill", page.Members); // не «ann+bob» из кэша
        var checks = cache.Journal().HitChecks;
        Assert.Equal(1, checks.Checked);
        Assert.Equal(1, checks.Mismatched);
        var m = Assert.Single(checks.Mismatches);
        Assert.Equal("group-24", m.Key);
        Assert.Contains(CacheTags.Row("HubGroups", 24), m.Tags);
        Assert.Contains("bob", m.Difference);
        Assert.Contains("bill", m.Difference);
        Assert.Equal("ann+bill", (await cache.GetAsync<Page>("group-24"))!.Members); // запись пересобрана
    }

    [Fact]
    public async Task HonestNarrowing_Matches_AndServesTheCachedObject()
    {
        var cache = Cache();
        var first = await GroupPage(cache);
        await EditGroup17(cache, "bill"); // чужая правка честную страницу не касается

        var hit = await GroupPage(cache);

        Assert.Same(first, hit);
        Assert.Equal(1, cache.Journal().HitChecks.Checked);
        Assert.Equal(0, cache.Journal().HitChecks.Mismatched);
    }

    [Fact]
    public async Task TableLevelEntries_AreNotVerified()
    {
        var cache = Cache();
        await cache.GetOrCreateAsync("season-best", () =>
        {
            _builds++;
            CacheBuildScope.Current!.TouchTable("Results"); // блока нет — табличная запись
            return Task.FromResult(new Page("sb"));
        }, Ttl);

        await cache.GetOrCreateAsync("season-best", () => { _builds++; return Task.FromResult(new Page("sb")); }, Ttl);

        // Табличные записи доказаны К4; шум «от времени» в них сверке не нужен.
        Assert.Equal(1, _builds);
        Assert.Equal(0, cache.Journal().HitChecks.Checked);
    }

    /// <summary>
    /// Записи, читавшие служебные колонки (К4б.6): пока точность по колонкам включена, служебная
    /// правка сбрасывает их колонкой, а не таблицей, — их сверяет попадание. Выключена — они
    /// табличные, как в <see cref="TableLevelEntries_AreNotVerified"/>.
    /// </summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task ColumnLevelEntries_AreVerified_WhileColumnPrecisionIsOn(bool columnPrecision, int checkedCount)
    {
        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()),
            new CacheSettingsStub(hitVerifyPercent: 100, columnPrecision: columnPrecision));
        Task<Page> Card() => cache.GetOrCreateAsync("swimmer-card", () =>
        {
            _builds++;
            CacheBuildScope.Current!.TouchTable("Swimmers", ["LogligId"]); // карточка показывает привязку
            return Task.FromResult(new Page("card"));
        }, Ttl);

        await Card();
        await Card();

        Assert.Equal(checkedCount, cache.Journal().HitChecks.Checked);
        Assert.Equal(0, cache.Journal().HitChecks.Mismatched);
    }

    [Fact]
    public async Task ZeroPercent_NeverVerifies()
    {
        var cache = Cache(percent: 0);
        await GroupPage(cache, broken: true);
        await EditGroup17(cache, "bill");

        var page = await GroupPage(cache, broken: true);

        Assert.Equal("ann+bob", page.Members); // врёт — сверка выключена, как на проде
        Assert.Equal(1, _builds);
    }

    [Fact]
    public async Task NestedEntries_AreRebuiltToo_NotReadFromTheCache()
    {
        var cache = Cache();
        var styles = "free";
        var nestedBuilds = 0;
        Task<Page> Render() => cache.GetOrCreateAsync("group-24", async () =>
        {
            var scope = CacheBuildScope.Current!;
            using (scope.Narrow("HubGroups", [24], MembersTable)) scope.TouchTable("HubGroupMembers");
            var nested = await cache.GetOrCreateAsync("styles", () =>
            {
                nestedBuilds++;
                CacheBuildScope.Current!.TouchTable("Styles");
                return Task.FromResult(new Page(styles));
            }, Ttl);
            return new Page($"{_members[24]}/{nested.Members}");
        }, Ttl);

        await Render();
        styles = "fly"; // вложенная запись устарела, а сброс до неё не дошёл

        var page = await Render();

        // Сверка, прочитавшая вложенную запись из кэша, совпала бы сама с собой.
        Assert.Equal(2, nestedBuilds);
        Assert.Equal(1, cache.Journal().HitChecks.Mismatched);
        Assert.Equal("free", (await cache.GetAsync<Page>("styles"))!.Members); // сверка ничего не кладёт
        Assert.Equal("ann/free", page.Members); // пересобрана обычным путём — из той же вложенной записи
    }

    [Fact]
    public async Task EntryDroppedWhileVerifying_IsNotASuspicion()
    {
        var cache = Cache();
        var page = () => cache.GetOrCreateAsync("group-24", async () =>
        {
            var scope = CacheBuildScope.Current!;
            if (scope.IsVerification)
            {
                // Пока сверка строила ответ, правку группы 24 сохранили — и сброс дошёл.
                _members[24] = "anna";
                await cache.InvalidateTagsAsync(CacheTags.Table("HubGroupMembers"), CacheTags.Row("HubGroups", 24));
            }
            using (scope.Narrow("HubGroups", [24], MembersTable)) scope.TouchTable("HubGroupMembers");
            return new Page(_members[24]);
        }, Ttl);
        await page();

        await page();

        Assert.Equal(1, cache.Journal().HitChecks.Checked);
        Assert.Equal(0, cache.Journal().HitChecks.Mismatched);
    }

    [Fact]
    public void Difference_ShowsWhereTheJsonParted()
    {
        var d = MemoryCacheService.Difference("{\"members\":\"ann+bob\"}", "{\"members\":\"ann+bill\"}");

        Assert.StartsWith("с символа 17:", d);
        Assert.Contains("ann+bob", d);
        Assert.Contains("ann+bill", d);
    }
}
