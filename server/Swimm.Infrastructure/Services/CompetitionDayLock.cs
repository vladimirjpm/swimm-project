using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Общий замок «дни соревнования меняются» (Р123/Р124): деление по дням, сбор дней в событие и
/// «Затянуть этот день» берут <c>pg_advisory_xact_lock</c> одного класса по id соревнования, поэтому
/// встают друг за другом и после ожидания видят уже изменённый <c>EventId</c>. Только внутри
/// транзакции; на InMemory (тесты) — ничего.
/// </summary>
internal static class CompetitionDayLock
{
    /// <summary>Класс блокировки — тот же, что у деления после PR #283 ("SPLD").</summary>
    internal const int Class = 0x53504c44;

    internal static async Task AcquireAsync(SwimmDbContext db, IEnumerable<int> competitionIds, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        // По возрастанию id — два вызова с пересекающимися днями не возьмут замки накрест.
        foreach (var id in competitionIds.Distinct().Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({Class}, {id})", ct);
    }
}
