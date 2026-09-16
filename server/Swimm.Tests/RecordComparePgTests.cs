using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сравнение двух стран (11.3.1) на ЖИВОЙ базе. InMemory не годится по той же причине, что
/// у рейтинга: <c>Records.TimeMs</c> — вычисляемая колонка Postgres, и без неё сравнивать
/// было бы нечего.
///
/// Утверждаем инварианты, а не числа. Пропускается, если Postgres недоступен.
/// </summary>
public class RecordComparePgTests
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

    private sealed class NoopCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static RecordRepository Repo(SwimmReadDbContext db) => new(db, new NoopCacheService());

    private static RecordCompareQuery Q(string a = "ISR", string b = "USA",
        string? pool = null, string? gender = null)
        => RecordCompareQuery.Create(a, b, pool, gender);

    /// <summary>
    /// Счёт согласован сам с собой: побед плюс ничьих ровно столько, сколько общих дисциплин,
    /// а строк — столько же плюс односторонние.
    /// </summary>
    [Fact]
    public async Task Compare_ScoreAddsUp()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var cmp = await Repo(db).GetCompareAsync(Q());
        if (cmp.Rows.Count == 0) return;

        var s = cmp.Score;
        Assert.Equal(s.Compared, s.A + s.B + s.Tie);
        Assert.Equal(cmp.Rows.Count, s.Compared + s.AOnly + s.BOnly);
    }

    /// <summary>
    /// Главное правило этапа на живых данных: у строки «нет данных» ровно одна сторона пуста
    /// (или обе), и она не приписана никому в победу.
    /// </summary>
    [Fact]
    public async Task Compare_NoDataRows_HaveAMissingSide()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var cmp = await Repo(db).GetCompareAsync(Q());
        if (cmp.Rows.Count == 0) return;

        foreach (var row in cmp.Rows)
        {
            if (row.Outcome == "no_data")
            {
                Assert.True(row.A == null || row.B == null);
                Assert.Null(row.DeltaMs);
            }
            else
            {
                Assert.NotNull(row.A);
                Assert.NotNull(row.B);
                Assert.NotNull(row.DeltaMs);
            }
        }
    }

    /// <summary>Исход всегда согласован со временами — «быстрее» значит меньшее время.</summary>
    [Fact]
    public async Task Compare_OutcomeMatchesTimes()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var cmp = await Repo(db).GetCompareAsync(Q());

        foreach (var row in cmp.Rows.Where(r => r.A != null && r.B != null))
        {
            var expected = row.A!.TimeMs < row.B!.TimeMs ? "a"
                : row.A.TimeMs > row.B.TimeMs ? "b" : "tie";
            Assert.Equal(expected, row.Outcome);
            Assert.Equal(Math.Abs(row.A.TimeMs - row.B.TimeMs), row.DeltaMs);
        }
    }

    /// <summary>Приёмка 11.3.2: обмен странами местами даёт зеркальный результат.</summary>
    [Fact]
    public async Task Compare_SwapIsMirrored()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var repo = Repo(db);
        var direct = await repo.GetCompareAsync(Q("ISR", "USA"));
        var mirror = await repo.GetCompareAsync(Q("USA", "ISR"));
        if (direct.Rows.Count == 0) return;

        Assert.Equal(direct.Rows.Count, mirror.Rows.Count);
        Assert.Equal(direct.Score.A, mirror.Score.B);
        Assert.Equal(direct.Score.B, mirror.Score.A);
        Assert.Equal(direct.Score.AOnly, mirror.Score.BOnly);
        Assert.Equal(direct.Score.Compared, mirror.Score.Compared);
    }

    /// <summary>Разрез сужает выборку и не меняет исходов у оставшихся дисциплин.</summary>
    [Fact]
    public async Task Compare_PoolFilter_NarrowsWithoutChangingOutcomes()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var repo = Repo(db);
        var all = await repo.GetCompareAsync(Q());
        var lcm = await repo.GetCompareAsync(Q(pool: "50m"));
        if (all.Rows.Count == 0 || lcm.Rows.Count == 0) return;

        Assert.All(lcm.Rows, r => Assert.Equal("50m", r.PoolType));
        Assert.True(lcm.Rows.Count < all.Rows.Count);

        foreach (var row in lcm.Rows)
        {
            var same = all.Rows.Single(r => r.Style == row.Style && r.Distance == row.Distance
                                         && r.Gender == row.Gender && r.PoolType == row.PoolType);
            Assert.Equal(same.Outcome, row.Outcome);
            Assert.Equal(same.DeltaMs, row.DeltaMs);
        }
    }

    /// <summary>
    /// Страна без единого рекорда — сравнение не падает: все дисциплины второй стороны видны
    /// строками «нет данных», счёт нулевой.
    /// </summary>
    [Fact]
    public async Task Compare_UnknownCountry_GivesNoDataRows()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var cmp = await Repo(db).GetCompareAsync(Q("ZZZ", "USA"));

        Assert.Equal(0, cmp.Score.Compared);
        Assert.Equal(0, cmp.Score.A);
        Assert.Equal(0, cmp.Score.B);
        Assert.All(cmp.Rows, r => Assert.Null(r.A));
        Assert.All(cmp.Rows, r => Assert.Equal("no_data", r.Outcome));
    }
}
