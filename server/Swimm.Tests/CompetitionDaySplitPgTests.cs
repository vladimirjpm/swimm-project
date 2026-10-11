using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Деление многодневки (multiday-split-plan, М2) против настоящего Postgres: InMemory не видит
/// частичный уникальный индекс на <c>Competitions.OrgCompId</c>, уникальный
/// <c>(Name, Date, PoolType)</c>, уникальную пару категории и FK — а новый путь записи задевает
/// их все (память «InMemory не видит CK Postgres»). Заводит свои строки и удаляет их в finally;
/// пропуск, если базы нет.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class CompetitionDaySplitPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    private sealed class FixedDates(DisciplineDates dates) : IDisciplineDateSource
    {
        public Task<DisciplineDates> FetchAsync(int logligId, CancellationToken ct = default) => Task.FromResult(dates);
    }

    private sealed class NoRecalc : ICompetitionRecalculationService
    {
        public Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RecalculateAllParaPointsAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task Split_PassesPostgresConstraints_CompIdStaysOnTheExistingDay()
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск

        var tag = $"pg-split-{Guid.NewGuid():N}"[..20];
        var orgCompId = 900_000_000 + Random.Shared.Next(1, 99_999_999);
        var day1 = new DateOnly(2099, 1, 3);
        var day2 = new DateOnly(2099, 1, 4);

        var free = await db.Styles.FirstAsync(s => s.Name == "freestyle");
        var swimmerId = await db.Swimmers.Select(s => s.Id).FirstAsync();
        var clubId = await db.Clubs.Select(c => c.Id).FirstAsync();
        var categoryId = await db.Categories.Select(c => c.Id).FirstAsync();

        var comp = new Competition { Name = tag, Date = "04/01/2099", PoolType = "25", OrgCompId = orgCompId, IsMasters = true };
        db.Competitions.Add(comp);
        await db.SaveChangesAsync();
        var created = new List<int> { comp.Id };
        int? eventId = null;
        try
        {
            db.CategoryCompetitions.Add(new CategoryCompetition { CategoryId = categoryId, CompetitionId = comp.Id, DisplayOrder = 1 });
            ResultRecord Row(string distance) => new()
            {
                CompetitionId = comp.Id, SwimmerId = swimmerId, ClubId = clubId, StyleId = free.Id,
                Distance = distance, Gender = "female", CompetitionDate = new DateTime(2099, 1, 4),
            };
            var first = Row("400");
            var second = Row("100");
            db.Results.AddRange(first, second);
            await db.SaveChangesAsync();

            var dates = new DisciplineDates(
            [
                (new DisciplineDayKey("freestyle", "400", "female"), day1),
                (new DisciplineDayKey("freestyle", "100", "female"), day2),
            ]);
            var report = await new CompetitionDaySplitService(db, new FixedDates(dates), new NoRecalc())
                .SplitAsync(comp.Id, logligId: 1, apply: true);

            Assert.Null(report.Refusal);
            eventId = report.EventId;
            var newDayId = report.Days.Single(d => !d.Existing).CompetitionId!.Value;
            created.Add(newDayId);

            await using var check = TryCreate()!;
            var days = await check.Competitions.AsNoTracking().Where(c => c.EventId == eventId).OrderBy(c => c.DayNumber).ToListAsync();
            Assert.Equal([(newDayId, "03/01/2099", (int?)null), (comp.Id, "04/01/2099", (int?)orgCompId)],
                days.Select(d => (d.Id, d.Date, d.OrgCompId)));
            Assert.Equal(orgCompId, (await check.CompetitionEvents.SingleAsync(e => e.Id == eventId)).OrgCompId);
            Assert.Equal(newDayId, (await check.Results.SingleAsync(r => r.Id == first.Id)).CompetitionId);
            Assert.Equal(comp.Id, (await check.Results.SingleAsync(r => r.Id == second.Id)).CompetitionId);
            Assert.True(await check.CategoryCompetitions.AnyAsync(cc => cc.CategoryId == categoryId && cc.CompetitionId == newDayId));
        }
        finally
        {
            await using var clean = TryCreate()!;
            await clean.Results.Where(r => created.Contains(r.CompetitionId)).ExecuteDeleteAsync();
            await clean.Competitions.Where(c => created.Contains(c.Id) || c.Name == tag).ExecuteDeleteAsync();
            if (eventId is int e) await clean.CompetitionEvents.Where(x => x.Id == e).ExecuteDeleteAsync();
        }
    }
}
