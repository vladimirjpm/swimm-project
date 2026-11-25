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

        var reasons = await ReasonsAsync(byCompetition.Select(c => c.CompetitionId!.Value).ToList(), ct);

        return new DataCheckOutcome(total, byCompetition
            .Select(c => new DataCheckItem(
                "Competition", c.CompetitionId ?? 0,
                $"{c.CompName}: без результата {c.NoShow} из {totals.GetValueOrDefault(c.CompetitionId ?? 0, c.NoShow)}",
                Details(c.OrgCompId, reasons.GetValueOrDefault(c.CompetitionId ?? 0, [])),
                $"/Admin/Competitions?q={c.OrgCompId}",
                PublicRoutes.Competition(c.CompetitionId ?? 0)))
            .ToList());
    }

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
        List<int> competitionIds, CancellationToken ct)
    {
        var entries = await db.CompetitionEntries.AsNoTracking()
            .Where(e => e.Status == CompetitionEntryStatus.NoShow && e.CompetitionId != null
                        && competitionIds.Contains(e.CompetitionId.Value))
            .Select(e => new
            {
                CompetitionId = e.CompetitionId!.Value, e.SwimmerId, e.ClubId, e.StyleId, Style = e.Style.Name,
                e.Distance, e.Gender, e.Heat, e.Lane,
                Swimmer = new NoShowReasons.Person(e.SwimmerId, e.Swimmer.LastName, e.Swimmer.FirstName,
                    e.Swimmer.BirthYear, e.Swimmer.LogligId, e.Swimmer.ClubId),
            })
            .ToListAsync(ct);
        if (entries.Count == 0) return [];

        var styleIds = entries.Select(e => e.StyleId).Distinct().ToList();
        var ownEntries = (await db.CompetitionEntries.AsNoTracking()
                .Where(e => e.CompetitionId != null && competitionIds.Contains(e.CompetitionId.Value) && styleIds.Contains(e.StyleId))
                .Select(e => new { CompetitionId = e.CompetitionId!.Value, e.SwimmerId, e.StyleId, e.Distance })
                .ToListAsync(ct))
            .Select(e => (e.CompetitionId, e.SwimmerId, e.StyleId, e.Distance))
            .ToHashSet();

        var rows = await db.Results.AsNoTracking()
            .Where(r => competitionIds.Contains(r.CompetitionId) && styleIds.Contains(r.StyleId))
            .Select(r => new
            {
                r.CompetitionId, r.StyleId, r.Distance, r.Heat, r.Lane, r.SwimmerId, r.ClubId, r.Gender,
                r.TimeOriginal, r.RelayId,
                Swimmer = new NoShowReasons.Person(r.SwimmerId, r.Swimmer.LastName, r.Swimmer.FirstName,
                    r.Swimmer.BirthYear, r.Swimmer.LogligId, r.Swimmer.ClubId),
            })
            .ToListAsync(ct);

        var relayIds = rows.Where(r => r.RelayId != null).Select(r => r.RelayId!.Value).Distinct().ToList();
        var members = (await db.RelayMembers.AsNoTracking()
                .Where(m => relayIds.Contains(m.RelayId))
                .OrderBy(m => m.LegOrder)
                .Select(m => new
                {
                    m.RelayId,
                    Swimmer = new NoShowReasons.Person(m.SwimmerId, m.Swimmer.LastName, m.Swimmer.FirstName,
                        m.Swimmer.BirthYear, m.Swimmer.LogligId, m.Swimmer.ClubId),
                })
                .ToListAsync(ct))
            .ToLookup(m => m.RelayId, m => m.Swimmer);

        var byLane = rows.ToLookup(r => (r.CompetitionId, r.StyleId, r.Distance, r.Heat, r.Lane));
        var byOwn = rows.Where(r => r.RelayId == null).ToLookup(r => (r.CompetitionId, r.StyleId, r.Distance, r.SwimmerId));

        return entries
            .OrderBy(e => e.Distance).ThenBy(e => e.Style).ThenBy(e => e.Heat).ThenBy(e => e.Lane)
            .GroupBy(e => e.CompetitionId)
            .ToDictionary(g => g.Key, g => g.Select(e =>
            {
                var isRelay = e.Distance.Contains('x', StringComparison.OrdinalIgnoreCase);
                var lane = byLane[(e.CompetitionId, e.StyleId, e.Distance, e.Heat, e.Lane)]
                    .Where(r => (r.RelayId != null) == isRelay)
                    .Select(r => new NoShowReasons.LaneRow(r.Swimmer, r.ClubId, r.Gender, r.TimeOriginal,
                        ownEntries.Contains((e.CompetitionId, r.SwimmerId, e.StyleId, e.Distance)),
                        r.RelayId is int id ? members[id].ToList() : []))
                    .ToList();
                var own = isRelay
                    ? []
                    : byOwn[(e.CompetitionId, e.StyleId, e.Distance, e.SwimmerId)]
                        .Where(r => r.Heat != e.Heat || r.Lane != e.Lane)
                        .Select(r => new NoShowReasons.OwnRow(r.Heat, r.Lane, r.TimeOriginal))
                        .ToList();
                var reason = NoShowReasons.Classify(
                    new NoShowReasons.Entry(e.Swimmer, e.ClubId, e.Gender, isRelay, e.Heat, e.Lane), lane, own);
                return ($"#{e.SwimmerId} {e.Swimmer.Name} ({e.Swimmer.BirthYear}), {e.Distance} {e.Style}, "
                        + $"заплыв {e.Heat}, дорожка {e.Lane}: {reason.Text}", reason.Kind);
            }).ToList());
    }
}
