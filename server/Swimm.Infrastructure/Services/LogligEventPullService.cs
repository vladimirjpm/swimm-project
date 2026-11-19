using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Затягивание соревнования из ПОСОБЫТИЙНОГО источника loglig (шаг 3 плана «склеенные
/// сессии», docs/data-integrity.md §10). Скачивает события, разбирает их провайдером,
/// сопоставляет имена с пловцами в БД и либо отчитывается (<see cref="DryRunAsync"/>),
/// либо импортирует (<see cref="ImportAsync"/>).
///
/// Зачем вообще: PDF-экспорт склеивает утреннюю и вечернюю сессии чемпионата в один список
/// и не знает раундов, а сайт держит их разными событиями. Имя у loglig печатается ОДНОЙ
/// ячейкой в порядке «имя фамилия», у нас поля раздельные — за разрезку отвечает
/// <see cref="LogligSwimmerNameResolver"/>, и неопознанные имена всегда видны в отчёте:
/// импорт такой строки завёл бы нового пловца вместо ремонта данных.
/// </summary>
public sealed class LogligEventPullService(
    SwimmDbContext db,
    ICompetitionDiscoveryProvider provider,
    ILogligImportBuilder builder,
    IImportService importer,
    ILogger<LogligEventPullService> logger) : ILogligEventPullService
{
    public async Task<LogligPullReport> DryRunAsync(int discoveredId, CancellationToken ct = default)
        => (await FetchAsync(discoveredId, ct)).Report;

    public async Task<(LogligPullReport Report, string ImportSummary)> ImportAsync(
        int discoveredId, CancellationToken ct = default)
    {
        var (report, events, resolver, competition, orgCompId, eventId) = await FetchAsync(discoveredId, ct);

        if (report.UnresolvedNames.Count > 0)
            throw new InvalidOperationException(
                $"Импорт остановлен: {report.UnresolvedNames.Count} имён не сопоставились с пловцами в БД. " +
                "Каждое такое имя завело бы нового пловца-двойника — сначала разберитесь с ними " +
                "(прогон --pull-events печатает список).");

        var json = builder.BuildResultsJson(
            events,
            new LogligImportContext("IL", competition.Name, competition.Date, competition.PoolType, competition.IsAward,
                EventDays: eventId is not null),
            row =>
            {
                var resolved = resolver.Resolve(row.FullName, row.BirthYear, row.Club);
                return (resolved.LastName, resolved.FirstName);
            });

        // DeleteMissing обязателен: у строк сменился ключ upsert (в нём теперь Round), и без
        // удаления старые безраундовые строки остались бы рядом дублями. PreserveRelays —
        // потому что эстафет источник не несёт, а «удалить лишнее» иначе снесло бы их.
        // Многодневка, уже разложенная по дням, импортируется в своё событие: строки садятся
        // в день по дате события loglig.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await importer.ImportAsync(
            stream,
            $"loglig-events-{orgCompId}.json",
            null,
            new ImportEventOptions(eventId, null,
                OverwriteExisting: true, DeleteMissing: true, PreserveRelays: true),
            orgCompId);

        var summary =
            $"строк в файле {result.TotalRows}, обновлено {result.Updated}, вставлено {result.Inserted}, " +
            $"удалено {result.Deleted}, ошибок {result.Errors}";
        logger.LogInformation("loglig import #{DiscoveredId}: {Summary}", discoveredId, summary);
        return (report, summary);
    }

    public async Task<LogligRelayPullReport> RelaysDryRunAsync(int discoveredId, CancellationToken ct = default)
        => (await FetchRelaysAsync(discoveredId, ct)).Report;

    public async Task<(LogligRelayPullReport Report, string ImportSummary)> ImportRelaysAsync(
        int discoveredId, CancellationToken ct = default)
    {
        var (report, events, legs, competition, orgCompId, eventId) = await FetchRelaysAsync(discoveredId, ct);
        if (!report.CanImport)
            throw new InvalidOperationException(
                $"Импорт эстафет остановлен: команд {report.Teams}, без состава {report.TeamsWithoutLegs}, " +
                $"ног не узнано {report.UnresolvedLegs.Count}. Неполный файл удалил бы часть эстафет — " +
                "сначала разберитесь (прогон --pull-events <id> --relays-only печатает список).");

        var json = builder.BuildRelayResultsJson(
            events,
            new LogligImportContext("IL", competition.Name, competition.Date, competition.PoolType, competition.IsAward,
                EventDays: eventId is not null),
            leg => legs.GetValueOrDefault(LegKey(leg)));

        // Только эстафеты: upsert с удалением исчезнувших среди эстафет, личные строки — как есть.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await importer.ImportAsync(
            stream,
            $"loglig-relays-{orgCompId}.json",
            null,
            new ImportEventOptions(eventId, null,
                OverwriteExisting: true, DeleteMissing: true, PreserveIndividuals: true),
            orgCompId);

        var summary =
            $"строк в файле {result.TotalRows}, обновлено {result.Updated}, вставлено {result.Inserted}, " +
            $"удалено {result.Deleted}, ошибок {result.Errors}";
        logger.LogInformation("loglig relays import #{DiscoveredId}: {Summary}", discoveredId, summary);
        return (report, summary);
    }

    private static string LegKey(LogligRelayLegDto leg) => $"{leg.LogligId}|{leg.FullName}";

    /// <summary>
    /// Эстафеты со страниц событий + сопоставление ног. Нога узнаётся по id карточки loglig
    /// (стартовый протокол и сверки проставляют его пловцам), иначе — по имени: ровно один
    /// пловец ростера соревнования с тем же набором слов имени. Год у ноги страница не печатает,
    /// поэтому обычный резолвер (имя + год) тут не годится.
    /// </summary>
    private async Task<(LogligRelayPullReport Report,
                        List<LogligEventResultsDto> Events,
                        Dictionary<string, LogligResolvedLeg> Legs,
                        (string Name, string Date, string PoolType, bool IsAward) Competition,
                        int? OrgCompId,
                        int? EventId)>
        FetchRelaysAsync(int discoveredId, CancellationToken ct)
    {
        var row = await db.DiscoveredCompetitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == discoveredId, ct)
            ?? throw new InvalidOperationException($"Запись автозабора #{discoveredId} не найдена");
        if (row.LogligId is not int logligId)
            throw new InvalidOperationException(
                $"У записи #{discoveredId} нет LogligId — пособытийного источника у неё нет");

        var days = await CompetitionIdentity.ResolveDaysAsync(db, row.OrgCompId, ct);
        if (days.Count == 0)
            throw new InvalidOperationException(
                $"Соревнование с OrgCompId {row.OrgCompId} ещё не импортировано — " +
                "эстафеты со страниц событий только ЗАМЕНЯЮТ уже загруженные.");
        var dayIds = days.Select(d => d.Id).ToList();
        var head = await db.Competitions.AsNoTracking()
            .Where(c => dayIds.Contains(c.Id))
            .OrderBy(c => c.Id)
            .Select(c => new { c.Name, c.Date, c.PoolType, c.IsAward, c.EventId })
            .FirstAsync(ct);

        var events = new List<LogligEventResultsDto>();
        foreach (var id in await provider.FetchEventIdsAsync(logligId, ct))
        {
            var ev = await provider.FetchEventResultsAsync(id, ct);
            if (ev.IsRelay) events.Add(ev);
        }

        // Ростер — все, кто есть у соревнования: результаты, ноги эстафет, заявки.
        var fromResults = await db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId)).Select(r => r.SwimmerId).ToListAsync(ct);
        var fromLegs = await db.RelayMembers.AsNoTracking()
            .Where(m => db.Results.Any(r => r.RelayId == m.RelayId && dayIds.Contains(r.CompetitionId)))
            .Select(m => m.SwimmerId).ToListAsync(ct);
        var fromEntries = await db.CompetitionEntries.AsNoTracking()
            .Where(e => e.CompetitionId != null && dayIds.Contains(e.CompetitionId.Value))
            .Select(e => e.SwimmerId).ToListAsync(ct);
        var rosterIds = fromResults.Concat(fromLegs).Concat(fromEntries).ToHashSet();
        var roster = await db.Swimmers.AsNoTracking().Where(s => rosterIds.Contains(s.Id))
            .Select(s => new { s.LastName, s.FirstName, s.BirthYear })
            .ToListAsync(ct);

        var legIds = events.SelectMany(e => e.Rows).SelectMany(r => r.RelayLegs ?? [])
            .Where(l => l.LogligId != null).Select(l => l.LogligId!.Value).Distinct().ToList();
        var byLoglig = (await db.Swimmers.AsNoTracking()
                .Where(s => s.LogligId != null && legIds.Contains(s.LogligId.Value) && s.LogligIdStatus != "Rejected")
                .Select(s => new { LogligId = s.LogligId!.Value, s.LastName, s.FirstName, s.BirthYear })
                .ToListAsync(ct))
            .GroupBy(s => s.LogligId)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());

        var resolved = new Dictionary<string, LogligResolvedLeg>();
        var unresolved = new List<string>();
        int teams = 0, withoutLegs = 0, legsTotal = 0, byId = 0, byName = 0;
        foreach (var ev in events)
        foreach (var r in ev.Rows)
        {
            teams++;
            if (r.RelayLegs is not { Count: >= 2 } legs) { withoutLegs++; continue; }
            foreach (var leg in legs)
            {
                legsTotal++;
                var key = LegKey(leg);
                if (leg.LogligId is int lid && byLoglig.TryGetValue(lid, out var s))
                {
                    resolved[key] = new LogligResolvedLeg(s.LastName, s.FirstName, s.BirthYear);
                    byId++;
                    continue;
                }
                var tokens = NameTokens(leg.FullName);
                var same = roster.Where(x => NameTokens($"{x.LastName} {x.FirstName}").SetEquals(tokens)).ToList();
                if (same.Count == 1)
                {
                    resolved[key] = new LogligResolvedLeg(same[0].LastName, same[0].FirstName, same[0].BirthYear);
                    byName++;
                    continue;
                }
                unresolved.Add($"{leg.FullName} ({leg.LogligId?.ToString() ?? "—"}) · {r.Club}");
            }
        }

        var ours = await db.Results.AsNoTracking()
            .CountAsync(r => dayIds.Contains(r.CompetitionId) && r.RelayId != null, ct);
        var report = new LogligRelayPullReport(
            events.FirstOrDefault()?.CompetitionName ?? string.Empty, events.Count, teams, withoutLegs,
            legsTotal, byId, byName, unresolved.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(), ours);

        return (report, events, resolved,
                (head.Name, head.Date, head.PoolType, head.IsAward), row.OrgCompId, head.EventId);
    }

    /// <summary>Слова имени без порядка; гереш любым символом — одна буква (Р100).</summary>
    private static HashSet<string> NameTokens(string name) =>
        name.Split([' ', '-', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Replace('\u05F3', '\'').Replace('\u2019', '\'').Replace('\u2018', '\'').Replace('`', '\''))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Скачивание + разбор + сопоставление имён — общая часть разведки и импорта.</summary>
    private async Task<(LogligPullReport Report,
                        List<LogligEventResultsDto> Events,
                        LogligSwimmerNameResolver Resolver,
                        (string Name, string Date, string PoolType, bool IsAward) Competition,
                        int? OrgCompId,
                        int? EventId)>
        FetchAsync(int discoveredId, CancellationToken ct)
    {
        var row = await db.DiscoveredCompetitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == discoveredId, ct)
            ?? throw new InvalidOperationException($"Запись автозабора #{discoveredId} не найдена");

        if (row.LogligId is not int logligId)
            throw new InvalidOperationException(
                $"У записи #{discoveredId} нет LogligId — пособытийного источника у неё нет");

        var competition = await db.Competitions.AsNoTracking()
            .Where(c => c.OrgCompId == row.OrgCompId)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Name, c.Date, c.PoolType, c.EventId })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Соревнование с OrgCompId {row.OrgCompId} ещё не импортировано — " +
                "пособытийный источник только ПЕРЕтягивает уже существующее.");

        var isAward = await db.Competitions.AsNoTracking()
            .Where(c => c.OrgCompId == row.OrgCompId)
            .Select(c => c.IsAward)
            .FirstOrDefaultAsync(ct);

        var eventIds = await provider.FetchEventIdsAsync(logligId, ct);
        logger.LogInformation("loglig {LogligId}: событий {Count}", logligId, eventIds.Count);

        var resolver = new LogligSwimmerNameResolver(await KnownSwimmersAsync(row.OrgCompId, competition.EventId, ct));

        var events = new List<LogligEventResultsDto>(eventIds.Count);
        var competitionName = string.Empty;
        var individualRows = 0;
        var relayEvents = 0;
        var byRound = new Dictionary<string, int>();
        var unresolved = new List<string>();
        var clubPoints = new Dictionary<string, int>();

        foreach (var eventId in eventIds)
        {
            var ev = await provider.FetchEventResultsAsync(eventId, ct);
            events.Add(ev);
            if (competitionName.Length == 0) competitionName = ev.CompetitionName;

            if (ev.IsRelay)
            {
                relayEvents++;
                continue;   // состав команды страница события не печатает — эстафеты от PDF
            }

            foreach (var r in ev.Rows)
            {
                individualRows++;
                byRound[r.Round] = byRound.GetValueOrDefault(r.Round) + 1;
                if (r.ClubPoints is int points and > 0)
                    clubPoints[r.Club] = clubPoints.GetValueOrDefault(r.Club) + points;

                if (!resolver.Resolve(r.FullName, r.BirthYear, r.Club).Matched)
                    unresolved.Add($"{r.FullName} ({r.BirthYear}) · {r.Club}");
            }
        }

        var report = new LogligPullReport(
            competitionName, eventIds.Count, individualRows, relayEvents,
            byRound, unresolved.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            clubPoints);

        return (report, events, resolver,
                (competition.Name, competition.Date, competition.PoolType, isAward), row.OrgCompId,
                competition.EventId);
    }

    /// <summary>
    /// Пловцы, уже импортированные по этому соревнованию (по всем его дням) — кандидаты
    /// сопоставления для <see cref="LogligSwimmerNameResolver"/>. У разложенной многодневки
    /// OrgCompId стоит только на одном дне, поэтому дни ищутся по событию.
    /// </summary>
    private async Task<List<KnownSwimmerName>> KnownSwimmersAsync(int? orgCompId, int? eventId, CancellationToken ct)
    {
        var rows = await db.Results.AsNoTracking()
            .Where(r => eventId != null ? r.Competition!.EventId == eventId : r.Competition!.OrgCompId == orgCompId)
            .Select(r => new
            {
                r.Swimmer!.LastName,
                r.Swimmer.FirstName,
                r.Swimmer.BirthYear,
                Club = r.Club!.Name
            })
            .Distinct()
            .ToListAsync(ct);

        return rows
            .Select(r => new KnownSwimmerName(r.LastName, r.FirstName, r.BirthYear, r.Club))
            .ToList();
    }
}
