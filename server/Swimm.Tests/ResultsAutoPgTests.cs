using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Поля results-auto у строки «Входящих» (П5, миграция AddDiscoveredAutoPullVerdict) в НАСТОЯЩЕМ
/// Postgres под рантайм-ролью swimm_rw: CHECK вердикта, длины колонок, timestamptz. InMemory CHECK не
/// видит. Всё — внутри транзакции с откатом: таблица в seed-tables.txt, и коммит поднял бы её версию
/// синхронизации в общей локальной базе (как в DataVersionPgTests). Строки — <c>utest-…</c>.
/// Пропуск, если PG недоступен или миграция ещё не применена.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class ResultsAutoPgTests
{
    private const string Runtime =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm_rw;Password=swimm_rw_local_dev";

    private static SwimmDbContext NewDb() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Runtime).Options);

    private static async Task<bool> Available()
    {
        await using var db = NewDb();
        try
        {
            if (!await db.Database.CanConnectAsync()) return false;
            await db.DiscoveredCompetitions.Select(d => d.AutoPullVerdict).AnyAsync(); // нет колонки — миграции нет
            return true;
        }
        catch { return false; }
    }

    private static DiscoveredCompetition NewRow(string? verdict) => new()
    {
        // OrgCompId настоящих стартов — пятизначные; 9xxxxxxxx не пересечётся (UNIQUE).
        OrgCompId = 900_000_000 + Random.Shared.Next(1, 99_999_999),
        Name = "utest-results-auto-" + Guid.NewGuid().ToString("N")[..8],
        DateStart = DateTime.UtcNow.Date, DateEnd = DateTime.UtcNow.Date,
        AutoPullVerdict = verdict,
        AutoPullBlockedReason = verdict is null ? null : new string('x', ResultsAutoRules.MaxReasonLength),
        AutoPullCheckedAt = DateTime.UtcNow,
        AutoPullPoolType = "25m",
    };

    [Fact]
    public async Task Every_known_verdict_is_accepted_and_round_trips()
    {
        if (!await Available()) return;
        await using var db = NewDb();
        await using var tx = await db.Database.BeginTransactionAsync();

        var rows = ResultsAutoVerdicts.All.Select(v => NewRow(v)).Append(NewRow(null)).ToList();
        db.DiscoveredCompetitions.AddRange(rows);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var names = rows.Select(r => r.Name).ToList();
        var back = await db.DiscoveredCompetitions.AsNoTracking().Where(d => names.Contains(d.Name)).ToListAsync();
        Assert.Equal(rows.Count, back.Count);
        Assert.Equal(
            ResultsAutoVerdicts.All.Order(),
            back.Where(b => b.AutoPullVerdict != null).Select(b => b.AutoPullVerdict!).Order());
        Assert.All(back, b => Assert.Equal(DateTimeKind.Utc, b.AutoPullCheckedAt!.Value.Kind));
        Assert.Contains(back, b => b.AutoPullBlockedReason?.Length == ResultsAutoRules.MaxReasonLength);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Unknown_verdict_is_rejected_by_check()
    {
        if (!await Available()) return;
        await using var db = NewDb();
        await using var tx = await db.Database.BeginTransactionAsync();

        db.DiscoveredCompetitions.Add(NewRow("would-import"));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CK_Sys_DiscoveredCompetitions_AutoPullVerdict", ex.InnerException?.Message ?? ex.Message);

        await tx.RollbackAsync();
    }
}
