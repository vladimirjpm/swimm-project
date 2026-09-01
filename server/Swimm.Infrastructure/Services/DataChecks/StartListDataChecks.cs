using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services.DataChecks;

/// <summary>
/// Проверки по стартовым протоколам (docs/plans/start-list-plan.md, шаг С9).
///
/// Заявка, которой не нашлось результата, — не мусор, а НАХОДКА: это либо честная неявка,
/// либо промах импорта, и различить их можно только глазами. Ровно ради этого числа заявки
/// и не стираются после соревнования.
/// </summary>
public sealed class NoShowUnmatchedCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "entries.no-show-unmatched";
    public string Title => "Заявки без результата";

    public string Description =>
        "Пловец был в стартовом протоколе, а в протоколе результатов его нет. Обычно это " +
        "честная неявка (снялся в день старта), но так же выглядит и промах импорта: " +
        "строка уехала в чужой заплыв либо пловец задвоился. Смотреть стоит выбросы — " +
        "когда у одного соревнования неявок непривычно много.";

    /// <summary>
    /// Предупреждение, а не ошибка: неявки — нормальная жизнь соревнования, и красить их
    /// в красное значит приучить не смотреть на реестр. Тревожит не факт, а масштаб.
    /// </summary>
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        // Только там, где протокол УЖЕ импортирован: заявка со статусом entered ещё ничего
        // не значит — соревнование могло не состояться или просто не быть затянутым.
        var q = db.CompetitionEntries.AsNoTracking()
            .Where(e => e.Status == CompetitionEntryStatus.NoShow && e.CompetitionId != null);

        var total = await q.CountAsync(ct);
        if (total == 0) return DataCheckOutcome.Empty;

        // Группируем по соревнованию: пятьдесят строк «Иванов не приплыл» человеку
        // бесполезны, а «на старте X неявок 40 из 120» — сразу вопрос.
        var byCompetition = await q
            .GroupBy(e => new { e.OrgCompId, e.CompetitionId, e.CompName })
            .Select(g => new
            {
                g.Key.OrgCompId,
                g.Key.CompetitionId,
                g.Key.CompName,
                NoShow = g.Count()
            })
            .OrderByDescending(x => x.NoShow)
            .Take(50)
            .ToListAsync(ct);

        var totals = await db.CompetitionEntries.AsNoTracking()
            .Where(e => e.CompetitionId != null)
            .GroupBy(e => e.CompetitionId)
            .Select(g => new { CompetitionId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.CompetitionId!.Value, x => x.Total, ct);

        return new DataCheckOutcome(total, byCompetition
            .Select(c => new DataCheckItem(
                "Competition", c.CompetitionId ?? 0,
                $"{c.CompName}: без результата {c.NoShow} из {totals.GetValueOrDefault(c.CompetitionId ?? 0, c.NoShow)}",
                $"compID {c.OrgCompId}",
                $"/Admin/Competitions?search={c.OrgCompId}",
                PublicRoutes.Competition(c.CompetitionId ?? 0)))
            .ToList());
    }
}
