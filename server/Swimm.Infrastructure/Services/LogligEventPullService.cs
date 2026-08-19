using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Разведка пособытийного источника loglig (шаг 3 плана «склеенные сессии»,
/// docs/data-integrity.md §10). Скачивает события соревнования, разбирает их
/// провайдером источника и отчитывается, что получилось бы при импорте.
///
/// Ничего не пишет СОЗНАТЕЛЬНО: прежде чем менять данные, надо увидеть две вещи —
/// сходятся ли официальные очки с нашим зачётом и все ли имена сопоставляются с пловцами
/// в БД. Имя у loglig печатается ОДНОЙ ячейкой в порядке «имя фамилия», а у нас поля
/// раздельные; неверная разрезка завела бы дубли пловцов вместо ремонта данных.
/// </summary>
public sealed class LogligEventPullService(
    SwimmDbContext db,
    ICompetitionDiscoveryProvider provider,
    ILogger<LogligEventPullService> logger) : ILogligEventPullService
{
    public async Task<LogligPullReport> DryRunAsync(int discoveredId, CancellationToken ct = default)
    {
        var row = await db.DiscoveredCompetitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == discoveredId, ct)
            ?? throw new InvalidOperationException($"Запись автозабора #{discoveredId} не найдена");

        if (row.LogligId is not int logligId)
            throw new InvalidOperationException(
                $"У записи #{discoveredId} нет LogligId — пособытийного источника у неё нет");

        var eventIds = await provider.FetchEventIdsAsync(logligId, ct);
        logger.LogInformation("loglig {LogligId}: событий {Count}", logligId, eventIds.Count);

        // Имена пловцов, уже известные по этому соревнованию: пособытийный источник печатает
        // «имя фамилия» одной строкой, и разрезать её надёжно можно только сверкой с базой.
        var resolver = new LogligSwimmerNameResolver(await KnownSwimmersAsync(row.OrgCompId, ct));

        var competitionName = string.Empty;
        var individualRows = 0;
        var relayEvents = 0;
        var byRound = new Dictionary<string, int>();
        var unresolved = new List<string>();
        var clubPoints = new Dictionary<string, int>();

        foreach (var eventId in eventIds)
        {
            var ev = await provider.FetchEventResultsAsync(eventId, ct);
            if (competitionName.Length == 0) competitionName = ev.CompetitionName;

            if (ev.IsRelay)
            {
                relayEvents++;
                continue;   // состав команды страница события не печатает — см. отчёт
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

        return new LogligPullReport(
            competitionName, eventIds.Count, individualRows, relayEvents,
            byRound, unresolved.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            clubPoints);
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
