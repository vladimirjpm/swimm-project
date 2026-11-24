using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services.DataChecks;

/// <summary>
/// Два игрока loglig на одной карточке пловца (Р122, И-69).
///
/// У личной строки есть <c>Results.LogligPlayerId</c> — игрок, которому она принадлежит на сайте
/// организатора (пишет шаг после импорта, только по доказанной паре «заплыв + дорожка + время»).
/// Один человек на loglig — один игрок, поэтому два разных id у одной карточки в одном
/// соревновании значат, что импорт слил в неё двух людей: 24.11.2026 так слились братья
/// «מחמוד» и «מוחמד» בדארנה (игроки 240949 и 240950) — запасной путь «другое написание» (Р79).
///
/// Только читает: разделение — ручное решение. Находка даёт всё для него: имена и id игроков и
/// раскладку «наша строка → игрок».
/// </summary>
public sealed class SwimmerTwoLogligPlayersCheck(SwimmDbContext db) : IDataCheck
{
    private const int MaxItems = 50;
    private const int DetailsLimit = 1000;

    public string Id => "swimmers.two-loglig-players";
    public string Title => "Два игрока loglig на одной карточке";

    public string Description =>
        "В одном соревновании строки одного нашего пловца принадлежат на loglig РАЗНЫМ игрокам " +
        "(id игрока взят со страницы дисциплины по заплыву, дорожке и времени). Значит, в карточке " +
        "слиты два человека — обычно братья или тёзки, которых импорт принял за одного (И-69). " +
        "В находке — оба игрока и чьи какие строки: завести второму карточку и перенести его строки. " +
        "Само ничего не разделяется.";

    public DataCheckSeverity Severity => DataCheckSeverity.Error;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        // Соревнование — день; многодневное событие сводится ниже, уже по найденным пловцам.
        var pairs = await db.Results.AsNoTracking()
            .Where(r => r.LogligPlayerId != null && r.RelayId == null)
            .GroupBy(r => new { r.CompetitionId, r.SwimmerId })
            .Where(g => g.Min(r => r.LogligPlayerId) != g.Max(r => r.LogligPlayerId))
            .Select(g => new { g.Key.CompetitionId, g.Key.SwimmerId })
            .ToListAsync(ct);
        if (pairs.Count == 0) return DataCheckOutcome.Empty;

        var swimmerIds = pairs.Select(p => p.SwimmerId).Distinct().OrderBy(id => id).ToList();
        var shown = swimmerIds.Take(MaxItems).ToList();
        var competitionIds = pairs.Where(p => shown.Contains(p.SwimmerId)).Select(p => p.CompetitionId).Distinct().ToList();

        var rows = await db.Results.AsNoTracking()
            .Where(r => shown.Contains(r.SwimmerId) && competitionIds.Contains(r.CompetitionId)
                        && r.RelayId == null && r.LogligPlayerId != null)
            .Select(r => new
            {
                r.Id, r.SwimmerId, r.CompetitionId, PlayerId = r.LogligPlayerId!.Value,
                Style = r.Style!.Name, r.Distance, r.Heat, r.Lane, r.TimeOriginal, r.TimeFailNote,
            })
            .ToListAsync(ct);

        var swimmers = await db.Swimmers.AsNoTracking().Where(s => shown.Contains(s.Id))
            .Select(s => new { s.Id, s.LastName, s.FirstName, s.BirthYear })
            .ToDictionaryAsync(s => s.Id, ct);
        var competitions = await db.Competitions.AsNoTracking().Where(c => competitionIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Date, c.OrgCompId })
            .ToDictionaryAsync(c => c.Id, ct);
        var playerIds = rows.Select(r => r.PlayerId).Distinct().ToList();
        var names = await db.LogligPlayers.AsNoTracking().Where(p => playerIds.Contains(p.LogligId))
            .ToDictionaryAsync(p => p.LogligId, p => p.FullName, ct);

        string Player(int id) => names.TryGetValue(id, out var n) ? $"{id} «{n}»" : id.ToString(CultureInfo.InvariantCulture);

        var items = new List<DataCheckItem>();
        foreach (var swimmerId in shown)
        {
            if (!swimmers.TryGetValue(swimmerId, out var s)) continue;
            var pairKeys = pairs.Where(p => p.SwimmerId == swimmerId).Select(p => p.CompetitionId).ToHashSet();
            var mine = rows.Where(r => r.SwimmerId == swimmerId && pairKeys.Contains(r.CompetitionId)).ToList();
            if (mine.Count == 0) continue;

            var name = $"{s.LastName} {s.FirstName}".Trim();
            var players = mine.Select(r => r.PlayerId).Distinct().OrderBy(id => id).ToList();
            var comps = pairKeys.OrderBy(id => id)
                .Select(id => competitions.TryGetValue(id, out var c) ? $"«{c.Name}» {c.Date} (#{id})" : $"#{id}")
                .ToList();

            // Раскладка «игрок: его строки» — по ней строки переносят механически.
            var details = string.Join(" ‖ ", players.Select(p =>
                $"{Player(p)}: " + string.Join("; ", mine.Where(r => r.PlayerId == p)
                    .OrderBy(r => r.CompetitionId).ThenBy(r => r.Distance).ThenBy(r => r.Style)
                    .Select(r => $"{r.Distance} {r.Style} з.{r.Heat} д.{r.Lane} " +
                                 $"{(string.IsNullOrWhiteSpace(r.TimeOriginal) ? r.TimeFailNote ?? "без времени" : r.TimeOriginal)} (строка {r.Id})"))));
            if (details.Length > DetailsLimit) details = details[..(DetailsLimit - 1)] + "…";

            var message = $"{name} ({s.BirthYear}) #{swimmerId}: строки {players.Count} игроков loglig — " +
                          $"{string.Join(", ", players.Select(Player))} — в {string.Join(", ", comps)}";
            if (message.Length > 500) message = message[..499] + "…";

            items.Add(new DataCheckItem(
                "Swimmer", swimmerId, message, details,
                $"/Admin/Swimmers/Edit?id={swimmerId}",
                PublicRoutes.Swimmer(swimmerId),
                SubjectName: name));
        }

        return new DataCheckOutcome(swimmerIds.Count, items);
    }
}
