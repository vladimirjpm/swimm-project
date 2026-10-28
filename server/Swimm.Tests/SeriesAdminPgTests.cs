using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Админка серий против НАСТОЯЩЕГО Postgres (docs/admin-pages/series.md): каскады удаления
/// серии → части/регламенты/награды/полосы/призы, SET NULL у дня, уникальность версии регламента
/// и перезапись полос награды — то, чего InMemory не видит. Пропуск, если PG недоступен.
///
/// Тест ходит в рабочую локальную базу: заводит свою серию и своё соревнование и в finally
/// удаляет их.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class SeriesAdminPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql(Conn, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .Options);

    private static SwimmDbContext? TryCreate()
    {
        var db = NewContext();
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    [Fact]
    public async Task SeriesLifecycle_OnPostgres()
    {
        await using var probe = TryCreate();
        if (probe == null) return; // PG недоступен — пропуск

        var tag = Guid.NewGuid().ToString("N")[..8];
        var comp = new Competition { Name = $"PG series test {tag}", Date = "23/10/2026", PoolType = "50m" };
        probe.Add(comp);
        await probe.SaveChangesAsync();
        int? seriesId = null;

        try
        {
            await using (var db = NewContext())
            {
                var repo = new SeriesAdminRepository(db);
                var created = await repo.CreateSeriesAsync(new SeriesInputDto($"PG series {tag}", null, " רודובסקי ;; נימרי "));
                Assert.True(created.Success, created.Error);
                seriesId = created.Id;
                Assert.False((await repo.CreateSeriesAsync(new SeriesInputDto($"PG series {tag}", null, null))).Success);

                var part = await repo.AddPartAsync(created.Id, new SeriesPartInputDto("צעירים", "Youth", 1, 9, 14));
                var reg = await repo.AddRegulationAsync(created.Id,
                    new SeriesRegulationInputDto(new DateOnly(2026, 9, 1), "https://example.test/t.pdf", null));
                Assert.True(part.Success && reg.Success);
                Assert.False((await repo.AddRegulationAsync(created.Id,
                    new SeriesRegulationInputDto(new DateOnly(2026, 9, 1), null, null))).Success);

                var award = await repo.AddAwardAsync(reg.Id, new SeriesAwardInputDto(part.Id, "Cups", 3, true, 1, 0,
                    [new SeriesAwardBandDto("female", 10, 11), new SeriesAwardBandDto("male", 10, 12)],
                    [new SeriesAwardPrizeDto(1, "Cup")]));
                Assert.True(award.Success, award.Error);

                // Перезапись полос и призов целиком.
                Assert.True((await repo.UpdateAwardAsync(award.Id, new SeriesAwardInputDto(part.Id, "Cups", 3, true, 3, 0,
                    [new SeriesAwardBandDto(null, 16, null)],
                    [new SeriesAwardPrizeDto(1, "1,000 ₪"), new SeriesAwardPrizeDto(2, "500 ₪")]))).Success);

                var clone = await repo.CloneRegulationAsync(reg.Id,
                    new SeriesRegulationInputDto(new DateOnly(2027, 9, 1), null, null));
                Assert.True(clone.Success, clone.Error);

                Assert.True((await repo.SetCompetitionPartAsync(comp.Id, part.Id)).Success);
                Assert.Equal(1, (await repo.ToggleRegulationCheckedAsync(comp.Id, "pg-test")).Id);
            }

            await using (var check = NewContext())
            {
                var series = await new SeriesAdminRepository(check).GetByIdAsync(seriesId!.Value);
                Assert.NotNull(series);
                Assert.Equal("רודובסקי; נימרי", series.MatchPhrases);
                Assert.Equal(2, series.Regulations.Count);
                Assert.All(series.Regulations, r =>
                {
                    var a = Assert.Single(r.Awards);
                    Assert.Equal([new SeriesAwardBandDto(null, 16, null)], a.Bands);
                    Assert.Equal(2, a.Prizes.Count);
                });
                var day = Assert.Single(series.EditionDays);
                Assert.Equal("pg-test", day.RegulationCheckedBy);
            }

            await using (var db = NewContext())
                Assert.True((await new SeriesAdminRepository(db).DeleteSeriesAsync(seriesId!.Value)).Success);

            await using (var check = NewContext())
            {
                var c = await check.Competitions.AsNoTracking().SingleAsync(x => x.Id == comp.Id);
                Assert.Null(c.SeriesPartId);
                Assert.Null(c.SeriesRegulationCheckedAt);
                Assert.False(await check.SeriesRegulations.AnyAsync(r => r.SeriesId == seriesId));
                Assert.False(await check.CompetitionSeriesParts.AnyAsync(p => p.SeriesId == seriesId));
                seriesId = null;
            }
        }
        finally
        {
            await using var cleanup = NewContext();
            if (seriesId is int sid)
                await cleanup.CompetitionSeries.Where(s => s.Id == sid).ExecuteDeleteAsync();
            await cleanup.Competitions.Where(c => c.Id == comp.Id).ExecuteDeleteAsync();
        }
    }
}
