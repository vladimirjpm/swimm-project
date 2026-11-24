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
/// «Одно соревнование» — событие целиком: дни многодневки сведены (братья могли плыть в разные
/// дни), одиночный старт — сам себе событие. Находка — на пару «пловец + соревнование»
/// (<see cref="DataCheckFixKinds.SwimmerInCompetition"/>): принятая находка одного старта (дубль
/// игрока на стороне loglig) не глушит ту же карточку в другом.
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
        "В одном соревновании (все его дни) строки одного нашего пловца принадлежат на loglig РАЗНЫМ " +
        "игрокам (id игрока взят со страницы дисциплины по заплыву, дорожке и времени). Значит, в " +
        "карточке слиты два человека — обычно братья или тёзки, которых импорт принял за одного (И-69). " +
        "В находке — оба игрока и чьи какие строки: завести второму карточку и перенести его строки. " +
        "Само ничего не разделяется. Если это один человек с двумя записями на loglig — «Принять как " +
        "есть»: решение относится только к этому соревнованию.";

    public DataCheckSeverity Severity => DataCheckSeverity.Error;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        // Ключ события: EventId у дней многодневки, у одиночного старта — минус его id
        // (отрицательное не пересечётся с EventId). Id игрока есть только у личных строк.
        var groups = await db.Results.AsNoTracking()
            .Where(r => r.LogligPlayerId != null)
            .GroupBy(r => new { r.SwimmerId, Event = r.Competition.EventId ?? -r.CompetitionId })
            .Where(g => g.Min(r => r.LogligPlayerId) != g.Max(r => r.LogligPlayerId))
            .Select(g => new { g.Key.SwimmerId, g.Key.Event })
            .ToListAsync(ct);
        if (groups.Count == 0) return DataCheckOutcome.Empty;

        var shown = groups.OrderBy(g => g.SwimmerId).ThenBy(g => g.Event).Take(MaxItems).ToList();
        var swimmerIds = shown.Select(g => g.SwimmerId).Distinct().ToList();
        var eventIds = shown.Where(g => g.Event > 0).Select(g => g.Event).Distinct().ToList();
        var singleIds = shown.Where(g => g.Event < 0).Select(g => -g.Event).Distinct().ToList();

        var competitions = await db.Competitions.AsNoTracking()
            .Where(c => singleIds.Contains(c.Id) || (c.EventId != null && eventIds.Contains(c.EventId.Value)))
            .Select(c => new { c.Id, c.Name, c.Date, c.EventId })
            .ToListAsync(ct);
        var competitionIds = competitions.Select(c => c.Id).ToList();
        int EventOf(int competitionId, int? eventId) => eventId ?? -competitionId;

        var rows = await db.Results.AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId) && competitionIds.Contains(r.CompetitionId)
                        && r.LogligPlayerId != null)
            .Select(r => new
            {
                r.Id, r.SwimmerId, r.CompetitionId, PlayerId = r.LogligPlayerId!.Value,
                Style = r.Style!.Name, r.Distance, r.Heat, r.Lane, r.TimeOriginal, r.TimeFailNote,
            })
            .ToListAsync(ct);

        var swimmers = await db.Swimmers.AsNoTracking().Where(s => swimmerIds.Contains(s.Id))
            .Select(s => new { s.Id, s.LastName, s.FirstName, s.BirthYear })
            .ToDictionaryAsync(s => s.Id, ct);
        var playerIds = rows.Select(r => r.PlayerId).Distinct().ToList();
        var names = await db.LogligPlayers.AsNoTracking().Where(p => playerIds.Contains(p.LogligId))
            .ToDictionaryAsync(p => p.LogligId, p => p.FullName, ct);

        string Player(int id) => names.TryGetValue(id, out var n) ? $"{id} «{n}»" : id.ToString(CultureInfo.InvariantCulture);

        var items = new List<DataCheckItem>();
        foreach (var g in shown)
        {
            if (!swimmers.TryGetValue(g.SwimmerId, out var s)) continue;
            var days = competitions.Where(c => EventOf(c.Id, c.EventId) == g.Event).OrderBy(c => c.Id).ToList();
            if (days.Count == 0) continue;
            var dayIds = days.Select(d => d.Id).ToHashSet();
            var mine = rows.Where(r => r.SwimmerId == g.SwimmerId && dayIds.Contains(r.CompetitionId)).ToList();
            if (mine.Count == 0) continue;

            var name = $"{s.LastName} {s.FirstName}".Trim();
            var players = mine.Select(r => r.PlayerId).Distinct().OrderBy(id => id).ToList();
            var multiDay = days.Count > 1;
            var where = multiDay
                ? $"«{days[0].Name}» {days[0].Date}–{days[^1].Date} (дни #{string.Join(", #", days.Select(d => d.Id))})"
                : $"«{days[0].Name}» {days[0].Date} (#{days[0].Id})";

            // Раскладка «игрок: его строки» — по ней строки переносят механически.
            var details = string.Join(" ‖ ", players.Select(p =>
                $"{Player(p)}: " + string.Join("; ", mine.Where(r => r.PlayerId == p)
                    .OrderBy(r => r.CompetitionId).ThenBy(r => r.Distance).ThenBy(r => r.Style)
                    .Select(r => (multiDay ? $"день #{r.CompetitionId} " : "") +
                                 $"{r.Distance} {r.Style} з.{r.Heat} д.{r.Lane} " +
                                 $"{(string.IsNullOrWhiteSpace(r.TimeOriginal) ? r.TimeFailNote ?? "без времени" : r.TimeOriginal)} (строка {r.Id})"))));
            if (details.Length > DetailsLimit) details = details[..(DetailsLimit - 1)] + "…";

            var message = $"{name} ({s.BirthYear}) #{g.SwimmerId}: строки {players.Count} игроков loglig — " +
                          $"{string.Join(", ", players.Select(Player))} — в {where}";
            if (message.Length > 500) message = message[..499] + "…";

            items.Add(new DataCheckItem(
                "Swimmer", g.SwimmerId, message, details,
                $"/Admin/Swimmers/Edit?id={g.SwimmerId}",
                PublicRoutes.Swimmer(g.SwimmerId),
                SubjectName: name,
                // Ключ находки — пловец + соревнование (первый день события).
                FixKind: DataCheckFixKinds.SwimmerInCompetition,
                FixEntityId: days[0].Id));
        }

        return new DataCheckOutcome(groups.Count, items);
    }
}
