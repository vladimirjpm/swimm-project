using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Рейтинг рекордов (11.2.1) на ЖИВОЙ базе. InMemory здесь не годится принципиально:
/// <c>Records.TimeMs</c> — вычисляемая колонка Postgres (<c>GENERATED … STORED</c>), на
/// InMemory-провайдере она осталась бы null, и тест сортировки проверял бы пустоту
/// (урок RelayMemberUpsertPgTests).
///
/// Утверждаем инварианты, а не числа: база живая, рекорды меняются.
/// Пропускается, если Postgres недоступен.
/// </summary>
public class RecordRankingPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmReadDbContext? TryCreate()
    {
        var db = new SwimmReadDbContext(
            new DbContextOptionsBuilder<SwimmReadDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    /// <summary>Кэш выключен: тест про запрос и рейтинг, а не про попадание в кэш.</summary>
    private sealed class NoopCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static RecordRepository Repo(SwimmReadDbContext db) => new(db, new NoopCacheService());

    /// <summary>Через фабрику, как ходит контроллер, — она же нормализует «4x50m» → «4X50m».</summary>
    private static RecordRankingQuery Query(
        string style = "freestyle", string distance = "50m", string gender = "male",
        string pool = "50m", IReadOnlyList<string>? regions = null, int limit = 250, int offset = 0)
        => RecordRankingQuery.Create(style, distance, gender, pool, regions, limit, offset);

    /// <summary>
    /// Главная приёмка этапа: сортировка верна на СМЕСИ форматов времени. В справочнике рядом
    /// лежат «59.99» и «01:00.01», и строковая сортировка поставила бы минутную первой.
    /// Дистанция взята такая, где обе формы встречаются гарантированно (100 м).
    /// </summary>
    [Fact]
    public async Task Ranking_SortsByRealTime_NotByString()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var ranking = await Repo(db).GetRankingAsync(Query(distance: "100m"));
        if (ranking.Rows.Count < 2) return;   // данных ещё нет — прогон по странам не делали

        // Времена не убывают…
        var times = ranking.Rows.Select(r => r.TimeMs).ToList();
        Assert.Equal(times.OrderBy(t => t).ToList(), times);

        // …и в выборке действительно есть обе формы записи, иначе тест ничего не доказал.
        Assert.Contains(ranking.Rows, r => r.Time.Contains(':'));
        Assert.Contains(ranking.Rows, r => !r.Time.Contains(':'));
    }

    /// <summary>
    /// Места идут по спортивному правилу и согласованы с порядком: место не убывает, а
    /// одинаковые времена стоят на одном месте.
    /// </summary>
    [Fact]
    public async Task Ranking_RanksAreConsistentWithTimes()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var ranking = await Repo(db).GetRankingAsync(Query());
        if (ranking.Rows.Count < 2) return;

        Assert.Equal(1, ranking.Rows[0].Rank);
        for (var i = 1; i < ranking.Rows.Count; i++)
        {
            var (prev, cur) = (ranking.Rows[i - 1], ranking.Rows[i]);
            Assert.True(cur.Rank >= prev.Rank);
            Assert.Equal(cur.TimeMs == prev.TimeMs, cur.Rank == prev.Rank);
        }
    }

    /// <summary>
    /// В рейтинге стран не должно быть мирового рекорда: он эталон, а не участник. Иначе
    /// «world» с пустым кодом региона стоял бы первой строкой в каждой дисциплине.
    /// </summary>
    [Fact]
    public async Task Ranking_ContainsOnlyCountries_WorldGoesSeparately()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var ranking = await Repo(db).GetRankingAsync(Query());
        if (ranking.Rows.Count == 0) return;

        Assert.All(ranking.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.RegionCode)));
        Assert.NotNull(ranking.World);
        Assert.True(ranking.World!.TimeMs > 0);
    }

    /// <summary>
    /// Фильтр стран сужает выборку и НЕ меняет смысл: у страны то же время и то же
    /// отставание, что в общем рейтинге. Место — своё, оно считается внутри выборки.
    /// </summary>
    [Fact]
    public async Task Ranking_RegionFilter_KeepsTimesAndDeltas()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var repo = Repo(db);
        var all = await repo.GetRankingAsync(Query());
        if (all.Rows.Count < 3) return;

        var picked = all.Rows.Take(3).Select(r => r.RegionCode).ToList();
        var narrowed = await repo.GetRankingAsync(Query(regions: picked));

        Assert.Equal(3, narrowed.Total);
        foreach (var row in narrowed.Rows)
        {
            var same = Assert.Single(all.Rows, r => r.RegionCode == row.RegionCode);
            Assert.Equal(same.TimeMs, row.TimeMs);
            Assert.Equal(same.BehindWorldMs, row.BehindWorldMs);
        }
    }

    /// <summary>
    /// Пагинация режет готовый рейтинг, а не пересчитывает его: место строки не зависит от
    /// того, какой страницей её попросили. Это и есть причина, по которой места считаются по
    /// всей дисциплине (RecordRankingBuilder).
    /// </summary>
    [Fact]
    public async Task Ranking_PagingKeepsRanksStable()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var repo = Repo(db);
        var all = await repo.GetRankingAsync(Query());
        if (all.Rows.Count < 5) return;

        var page = await repo.GetRankingAsync(Query(limit: 2, offset: 2));

        Assert.Equal(all.Total, page.Total);          // total — про рейтинг, не про страницу
        Assert.Equal(2, page.Rows.Count);
        Assert.Equal(all.Rows[2].Rank, page.Rows[0].Rank);
        Assert.Equal(all.Rows[2].RegionCode, page.Rows[0].RegionCode);
        Assert.Equal(all.Rows[3].Rank, page.Rows[1].Rank);
    }

    /// <summary>
    /// Дисциплина без мирового эталона (4×50 в длинной воде) — рейтинг есть, отставания нет.
    /// Проверено на данных 16.09.2026: у World Aquatics такого рекорда нет вовсе.
    /// </summary>
    [Fact]
    public async Task Ranking_WithoutWorldRecord_HasRowsButNoDeltas()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var ranking = await Repo(db).GetRankingAsync(Query(distance: "4x50m", pool: "50m"));
        if (ranking.Rows.Count == 0) return;

        Assert.Null(ranking.World);
        Assert.All(ranking.Rows, r => Assert.Null(r.BehindWorldMs));
    }

    /// <summary>Неизвестная дисциплина — пустой рейтинг, а не исключение.</summary>
    [Fact]
    public async Task Ranking_UnknownDiscipline_ReturnsEmpty()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var ranking = await Repo(db).GetRankingAsync(Query(style: "no-such-style"));

        Assert.Empty(ranking.Rows);
        Assert.Equal(0, ranking.Total);
        Assert.Null(ranking.World);
    }
}
