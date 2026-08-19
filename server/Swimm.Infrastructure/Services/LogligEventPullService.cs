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
        var (report, events, resolver, competition, orgCompId) = await FetchAsync(discoveredId, ct);

        if (report.UnresolvedNames.Count > 0)
            throw new InvalidOperationException(
                $"Импорт остановлен: {report.UnresolvedNames.Count} имён не сопоставились с пловцами в БД. " +
                "Каждое такое имя завело бы нового пловца-двойника — сначала разберитесь с ними " +
                "(прогон --pull-events печатает список).");

        var json = builder.BuildResultsJson(
            events,
            new LogligImportContext("IL", competition.Name, competition.Date, competition.PoolType, competition.IsAward),
            row =>
            {
                var resolved = resolver.Resolve(row.FullName, row.BirthYear, row.Club);
                return (resolved.LastName, resolved.FirstName);
            });

        // DeleteMissing обязателен: у строк сменился ключ upsert (в нём теперь Round), и без
        // удаления старые безраундовые строки остались бы рядом дублями. PreserveRelays —
        // потому что эстафет источник не несёт, а «удалить лишнее» иначе снесло бы их.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await importer.ImportAsync(
            stream,
            $"loglig-events-{orgCompId}.json",
            null,
            new ImportEventOptions(null, null,
                OverwriteExisting: true, DeleteMissing: true, PreserveRelays: true),
            orgCompId);

        var summary =
            $"строк в файле {result.TotalRows}, обновлено {result.Updated}, вставлено {result.Inserted}, " +
            $"удалено {result.Deleted}, ошибок {result.Errors}";
        logger.LogInformation("loglig import #{DiscoveredId}: {Summary}", discoveredId, summary);
        return (report, summary);
    }

    /// <summary>Скачивание + разбор + сопоставление имён — общая часть разведки и импорта.</summary>
    private async Task<(LogligPullReport Report,
                        List<LogligEventResultsDto> Events,
                        LogligSwimmerNameResolver Resolver,
                        (string Name, string Date, string PoolType, bool IsAward) Competition,
                        int? OrgCompId)>
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
            .Select(c => new { c.Name, c.Date, c.PoolType })
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

        var resolver = new LogligSwimmerNameResolver(await KnownSwimmersAsync(row.OrgCompId, ct));

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
                (competition.Name, competition.Date, competition.PoolType, isAward), row.OrgCompId);
    }

    /// <summary>
    /// Пловцы, уже импортированные по этому соревнованию (по всем его дням) — кандидаты
    /// сопоставления для <see cref="LogligSwimmerNameResolver"/>.
    /// </summary>
    private async Task<List<KnownSwimmerName>> KnownSwimmersAsync(int? orgCompId, CancellationToken ct)
    {
        var rows = await db.Results.AsNoTracking()
            .Where(r => r.Competition!.OrgCompId == orgCompId)
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
