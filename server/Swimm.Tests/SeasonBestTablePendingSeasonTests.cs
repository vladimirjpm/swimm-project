using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Таблица Season best — источник бейджа SB в протоколе и My media. Для сезона НОВЕЕ витринного
/// (идёт, но зимний чемпионат не проплыт) она пустая: иначе первый же старт нового сезона
/// объявлялся бы «быстрейшим в стране» — ровно то, ради чего заведён витринный сезон
/// (решение Влада 26.10.2026, docs/season-boundary-rule.md).
/// </summary>
public class SeasonBestTablePendingSeasonTests
{
    private sealed class FixedShowcase(int year) : IShowcaseSeasonProvider
    {
        public Task<int> CurrentStartYearAsync(CancellationToken ct = default) => Task.FromResult(year);
        public Task<int> StartYearAtAsync(DateTime now, CancellationToken ct = default) => Task.FromResult(year);
        public Task<ShowcaseSeasonNoticeDto?> PendingNoticeAsync(CancellationToken ct = default) =>
            Task.FromResult<ShowcaseSeasonNoticeDto?>(null);
        public Task<ShowcaseSeasonNoticeDto?> PendingNoticeAtAsync(DateTime now, CancellationToken ct = default) =>
            Task.FromResult<ShowcaseSeasonNoticeDto?>(null);
    }

    [Fact]
    public async Task SeasonNewerThanShowcase_HasNoReference_SoNoSbBadges()
    {
        await using var db = new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(nameof(SeasonNewerThanShowcase_HasNoReference_SoNoSbBadges)).Options);
        var repo = new SeasonBestRepository(db, new FixedShowcase(2025));

        var table = await repo.GetSeasonBestTableAsync(2026);

        Assert.Equal(2026, table.Season);
        Assert.Equal("2026/27", table.SeasonLabel);
        Assert.Empty(table.Data);
    }
}
