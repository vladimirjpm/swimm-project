using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Штамповка loglig-id по протоколу соревнования (см. <see cref="ILogligStampService"/>).
///
/// Три правила, которые здесь важнее скорости:
/// <list type="number">
/// <item>уже привязанного не трогаем — связь в базе поставил человек;</item>
/// <item>тёзки (одно имя + один год у нескольких пловцов) пропускаются: привязать не тому
/// хуже, чем не привязать вовсе;</item>
/// <item>занятый id не отбираем у другого пловца — это симптом дубля, разбирается дедупом.</item>
/// </list>
/// </summary>
public class LogligStampService : ILogligStampService
{
    /// <summary>
    /// Сколько заплывов протокола готовы обойти. У чемпионата их под сотню, и это ЕДИНСТВЕННЫЙ
    /// дорогой шаг: страница на заплыв. Импорт от него не зависит — штамповка идёт после.
    /// </summary>
    private const int MaxEvents = 200;

    private readonly SwimmDbContext _db;
    private readonly ICompetitionDiscoveryService _discovery;
    private readonly ILogligClient _loglig;
    private readonly ILogger<LogligStampService> _logger;

    public LogligStampService(
        SwimmDbContext db,
        ICompetitionDiscoveryService discovery,
        ILogligClient loglig,
        ILogger<LogligStampService> logger)
    {
        _db = db;
        _discovery = discovery;
        _loglig = loglig;
        _logger = logger;
    }

    public async Task<LogligStampReport> StampFromProtocolAsync(int orgCompId, CancellationToken ct = default)
    {
        var row = (await _discovery.GetAllAsync(ct)).FirstOrDefault(d => d.OrgCompId == orgCompId);
        if (row?.LogligId is not int competitionLogligId)
            return Empty("У соревнования нет loglig-id — брать участников неоткуда.");

        // Пловцы ИМЕННО этого соревнования: чужих трогать не за что.
        var swimmers = await _db.Results.AsNoTracking()
            .Where(r => r.Competition!.OrgCompId == orgCompId)
            .Select(r => r.Swimmer)
            .Distinct()
            .Select(s => new { s.Id, s.FirstName, s.LastName, s.BirthYear, s.LogligId })
            .ToListAsync(ct);

        if (swimmers.Count == 0)
            return Empty("У соревнования нет пловцов в базе — нечего привязывать.");

        var alreadyLinked = swimmers.Count(s => s.LogligId != null);
        var pending = swimmers.Where(s => s.LogligId is null).ToList();
        if (pending.Count == 0)
            return new LogligStampReport(swimmers.Count, alreadyLinked, 0, 0, [],
                $"Все {swimmers.Count} пловцов соревнования уже привязаны.");

        // Тёзки: ключ, за которым стоит больше одного пловца, к привязке не годится.
        var byKey = pending
            .GroupBy(s => LogligClient.ParticipantKey($"{s.FirstName} {s.LastName}", s.BirthYear))
            .ToDictionary(g => g.Key, g => g.ToList());

        var participants = await _loglig.GetCompetitionParticipantsAsync(
            competitionLogligId, byKey.Keys, MaxEvents, ct);

        if (participants.Count == 0)
            return new LogligStampReport(swimmers.Count, alreadyLinked, 0, pending.Count, [],
                "Участников на loglig прочитать не удалось — привязок нет.");

        // Кандидаты для нечёткого сопоставления: у сайта имя бывает полнее нашего
        // («אליה מאשה גדול» против «אליה גדול»), поэтому одного равенства ключей мало.
        var candidates = participants
            .Select(p => ((IReadOnlyCollection<string>)LogligClient.NameTokens(p.FullName), p.BirthYear, p.LogligId))
            .ToList();

        // Кто из этих id уже занят в базе (в том числе пловцами других соревнований).
        var candidateIds = participants.Select(p => p.LogligId).ToList();
        var taken = await _db.Swimmers.AsNoTracking()
            .Where(s => s.LogligId != null && candidateIds.Contains(s.LogligId!.Value))
            .Select(s => new { s.Id, s.LogligId, s.LastName, s.FirstName })
            .ToListAsync(ct);
        var takenBy = taken.ToDictionary(t => t.LogligId!.Value, t => t);

        var stamped = 0;
        var notFound = 0;
        var skipped = new List<string>();

        foreach (var (_, group) in byKey)
        {
            var tokens = LogligClient.NameTokens($"{group[0].FirstName} {group[0].LastName}");
            var found = TokenNameMatcher.ResolveSingle(candidates, tokens, group[0].BirthYear);
            if (found == 0)
            {
                notFound += group.Count;
                continue;
            }

            var logligId = found;

            if (group.Count > 1)
            {
                skipped.Add($"{group[0].LastName} {group[0].FirstName} ({group[0].BirthYear}): "
                            + $"в базе {group.Count} тёзки — привязку делает человек");
                continue;
            }

            if (takenBy.TryGetValue(logligId, out var holder))
            {
                skipped.Add($"{group[0].LastName} {group[0].FirstName}: loglig #{logligId} уже у "
                            + $"«{holder.LastName} {holder.FirstName}» #{holder.Id} — похоже на дубль пловца");
                continue;
            }

            var swimmer = await _db.Swimmers.FirstOrDefaultAsync(s => s.Id == group[0].Id, ct);
            if (swimmer is null || swimmer.LogligId != null) continue;

            swimmer.LogligId = logligId;
            swimmer.LogligIdStatus = "Verified";
            // Отдельный источник: привязку сделал не админ и не пользователь, а официальный
            // протокол старта — по нему её потом и отличают (и при нужде отвязывают пачкой).
            swimmer.LogligIdSource = "protocol";
            swimmer.LogligIdVerifiedAt = DateTime.UtcNow;
            takenBy[logligId] = new { swimmer.Id, swimmer.LogligId, swimmer.LastName, swimmer.FirstName };
            stamped++;
        }

        if (stamped > 0)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Гонка за уникальным индексом LogligId: сохранить пачкой не вышло — честно
                // сообщаем, а не делаем вид, что привязали.
                _logger.LogWarning(ex, "Штамповка loglig-id: конфликт уникальности (compID {OrgCompId})", orgCompId);
                return new LogligStampReport(swimmers.Count, alreadyLinked, 0, notFound,
                    [.. skipped, "Конфликт уникальности loglig-id — привязки не сохранены"],
                    "Штамповка не сохранилась: конфликт уникальности loglig-id.");
            }
        }

        var message = $"Пловцов соревнования {swimmers.Count}: уже привязаны {alreadyLinked}, "
                      + $"привязано сейчас {stamped}, не нашлось в протоколе {notFound}"
                      + (skipped.Count > 0 ? $", пропущено {skipped.Count}" : "") + ".";

        return new LogligStampReport(swimmers.Count, alreadyLinked, stamped, notFound, skipped, message);
    }

    /// <inheritdoc />
    public async Task<LogligStampBackfillReport> BackfillAsync(CancellationToken ct = default)
    {
        // Кандидаты — соревнования с compID, у которых есть хоть один непривязанный пловец.
        // Считаем в БД: ходить на сайт ради соревнования, где привязаны все, незачем.
        var pending = await _db.Results.AsNoTracking()
            .Where(r => r.Competition!.OrgCompId != null && r.Swimmer.LogligId == null)
            .Select(r => new { OrgCompId = r.Competition!.OrgCompId!.Value, r.Competition.Name })
            .Distinct()
            .ToListAsync(ct);

        var lines = new List<string>();
        var stamped = 0;
        var notFound = 0;
        var skipped = 0;
        var index = 0;

        foreach (var comp in pending.OrderBy(c => c.OrgCompId))
        {
            ct.ThrowIfCancellationRequested();
            index++;

            var report = await StampFromProtocolAsync(comp.OrgCompId, ct);
            stamped += report.Stamped;
            notFound += report.NotFound;
            skipped += report.Skipped.Count;

            lines.Add($"[{index}/{pending.Count}] compID {comp.OrgCompId} «{comp.Name}»: {report.Message}");
            _logger.LogInformation("Штамповка loglig-id [{Index}/{Total}] compID {OrgCompId}: {Message}",
                index, pending.Count, comp.OrgCompId, report.Message);
        }

        return new LogligStampBackfillReport(pending.Count, stamped, notFound, skipped, lines);
    }

    private static LogligStampReport Empty(string message) => new(0, 0, 0, 0, [], message);
}
