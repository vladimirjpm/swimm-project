using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Счётчики «затянуто / тянуть нечего / всего» на чипах сезонов и кнопках месяцев
/// (/Admin/Competitions).
///
/// Живой случай: февраль 2026 — 12 из 14, и обе оставшиеся строки затянуть НЕЛЬЗЯ
/// (у одной пустой протокол, у другой результаты не опубликованы — нет loglig-id).
/// «12 из 14» читалось как долг на две штуки, хотя месяц закрыт. Такие строки считаются
/// отдельным числом, и когда ждать больше нечего, вьюха ставит зелёную галочку.
///
/// Ошибка забора (LastError) сюда НЕ входит: это как раз повод вернуться.
/// </summary>
public class CompetitionPullCountersTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed class NoCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static DiscoveredCompetition Site(int id, int orgCompId, string name,
        int? logligId, DateTime? emptySourceAt = null, string? lastError = null) => new()
    {
        Id = id, OrgCompId = orgCompId, Name = name,
        DateStart = new DateTime(2026, 2, 6, 0, 0, 0, DateTimeKind.Utc),
        DateEnd = new DateTime(2026, 2, 6, 0, 0, 0, DateTimeKind.Utc),
        Status = "new", LogligId = logligId, EmptySourceAt = emptySourceAt, LastError = lastError,
    };

    [Fact]
    public async Task NothingToPull_CountedApart_SoTheMonthCanBeClosed()
    {
        await using var db = CreateDb(nameof(NothingToPull_CountedApart_SoTheMonthCanBeClosed));
        db.Competitions.Add(new Competition
        {
            Id = 1, Name = "Затянутое", Date = "06/02/2026", PoolType = "25m", OrgCompId = 100
        });
        db.DiscoveredCompetitions.AddRange(
            Site(10, 100, "Затянутое", logligId: 14042),
            // Протокол пуст — разбор ничего не нашёл, тянуть нечего.
            Site(11, 200, "Пустой протокол", logligId: 14119,
                emptySourceAt: new DateTime(2026, 2, 14, 0, 0, 0, DateTimeKind.Utc)),
            // Результаты не опубликованы: нет loglig-id, значит нет и файла.
            Site(12, 300, "Нет результатов на сайте", logligId: null));
        await db.SaveChangesAsync();

        var list = await new CompetitionAdminRepository(db, new NoCache())
            .GetUnifiedAsync(null, null, 2026, null, showSynthetic: false, month: null, 1, 20);

        var season = Assert.Single(list.SeasonCounts!);
        Assert.Equal(3, season.Total);
        Assert.Equal(1, season.Imported);
        Assert.Equal(2, season.NothingToPull);
        // Ждать больше нечего — вьюха на этом рисует ✓.
        Assert.Equal(0, season.Pending);

        // Та же тройка на кнопке месяца (февраль — индекс 1).
        Assert.Equal(3, list.MonthCounts[1]);
        Assert.Equal(1, list.MonthImported![1]);
        Assert.Equal(2, list.MonthNothingToPull![1]);
    }

    [Fact]
    public async Task RowWithFetchError_StillWaits_ItIsAReasonToComeBack()
    {
        await using var db = CreateDb(nameof(RowWithFetchError_StillWaits_ItIsAReasonToComeBack));
        db.DiscoveredCompetitions.Add(Site(10, 100, "Сорвался забор", logligId: 14042, lastError: "502"));
        await db.SaveChangesAsync();

        var list = await new CompetitionAdminRepository(db, new NoCache())
            .GetUnifiedAsync(null, null, 2026, null, showSynthetic: false, month: null, 1, 20);

        var season = Assert.Single(list.SeasonCounts!);
        Assert.Equal(0, season.NothingToPull);
        Assert.Equal(1, season.Pending);
    }
}
