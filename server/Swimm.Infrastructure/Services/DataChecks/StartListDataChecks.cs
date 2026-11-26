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

        // Total — в соревнованиях, как и находки (одна находка = одно соревнование), а не в
        // заявках: из Total вычитаются принятые находки, и 44 заявки − 7 принятых соревнований
        // дали дашборду 37 «открытых» при нуле в Health (26.11). Сколько заявок — в тексте находки.
        var total = await q.Select(e => e.CompetitionId).Distinct().CountAsync(ct);
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

        var reasons = await ReasonsAsync(byCompetition.Select(c => c.CompetitionId!.Value).ToList(), ct);

        return new DataCheckOutcome(total, byCompetition
            .Select(c => new DataCheckItem(
                "Competition", c.CompetitionId ?? 0,
                $"{c.CompName}: без результата {c.NoShow} из {totals.GetValueOrDefault(c.CompetitionId ?? 0, c.NoShow)}",
                Details(c.OrgCompId, reasons.GetValueOrDefault(c.CompetitionId ?? 0, [])),
                $"/Admin/Competitions?q={c.OrgCompId}",
                PublicRoutes.Competition(c.CompetitionId ?? 0),
                AutoAccept: AutoAcceptNote(c.NoShow, reasons.GetValueOrDefault(c.CompetitionId ?? 0, []))))
            .ToList());
    }

    /// <summary>
    /// Все неявки соревнования честные (<see cref="NoShowReasons.IsHonest"/>) — находку принимает
    /// сама проверка с этой причиной (решение Влада 25.11.2026); список по заявкам остаётся в Details.
    /// Хоть одна нечестная или причин меньше, чем неявок, — null: смотрит человек.
    /// </summary>
    internal static string? AutoAcceptNote(int noShow, IReadOnlyList<(string Line, NoShowReasons.Kind Kind)> reasons) =>
        reasons.Count > 0 && reasons.Count == noShow && reasons.All(r => NoShowReasons.IsHonest(r.Kind))
            ? "все неявки честные — " + string.Join(", ", reasons.GroupBy(r => r.Kind).OrderBy(g => g.Key)
                .Select(g => $"{NoShowReasons.Label(g.Key)} — {g.Count()}"))
            : null;

    /// <summary>Потолок колонки <c>Sys_DataCheckFindings.Details</c>.</summary>
    private const int DetailsMax = 4000;

    /// <summary>
    /// Первая строка — compID и сводка по случаям; дальше строка на заявку (страница прячет их под
    /// «причина по каждой заявке»). Не влезло в колонку — честное «… ещё N».
    /// </summary>
    internal static string Details(int orgCompId, IReadOnlyList<(string Line, NoShowReasons.Kind Kind)> reasons)
    {
        if (reasons.Count == 0) return $"compID {orgCompId}";
        var head = $"compID {orgCompId} · " + string.Join(", ", reasons.GroupBy(r => r.Kind).OrderBy(g => g.Key)
            .Select(g => $"{NoShowReasons.Label(g.Key)} — {g.Count()}"));

        var text = new System.Text.StringBuilder(head);
        for (var i = 0; i < reasons.Count; i++)
        {
            var line = "\n" + reasons[i].Line;
            var tail = $"\n… ещё {reasons.Count - i}";
            if (text.Length + line.Length + (i < reasons.Count - 1 ? tail.Length : 0) > DetailsMax)
            {
                text.Append(tail);
                break;
            }
            text.Append(line);
        }
        return text.Length <= DetailsMax ? text.ToString() : text.ToString()[..DetailsMax];
    }

    /// <summary>Причина на каждую неявку соревнований-дней <paramref name="competitionIds"/> — по базе, без сети.</summary>
    private async Task<Dictionary<int, List<(string Line, NoShowReasons.Kind Kind)>>> ReasonsAsync(
        List<int> competitionIds, CancellationToken ct) =>
        (await NoShowCases.LoadAsync(db, competitionIds, ct))
            .GroupBy(c => c.CompetitionId)
            .ToDictionary(g => g.Key, g => g.Select(c => (c.Line, c.Reason.Kind)).ToList());

}
