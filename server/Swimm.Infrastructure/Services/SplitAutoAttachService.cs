using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Доклейка промежуточных сама после импорта (решение Влада 23.11.2026). Правило доклейки — в
/// <see cref="ISplitAttachService"/> (тот же, что у кнопки и CLI); здесь только: какие дни, по
/// одному прогону на событие, исход и строка аудита каждому дню.
///
/// Аудит пишется ВСЕГДА, и при «нечего доклеивать», и при сбое (в отличие от кнопки, которая
/// пишет только записавший прогон): это единственный след автозапуска, переживающий перезапуск
/// процесса, и по нему шаг 9 чек-листа показывает итог.
/// </summary>
public class SplitAutoAttachService : ISplitAutoAttachService
{
    private readonly SwimmDbContext _db;
    private readonly ISplitAttachService _attach;
    private readonly IAdminAuditService _audit;
    private readonly ILogger<SplitAutoAttachService> _logger;

    public SplitAutoAttachService(SwimmDbContext db, ISplitAttachService attach, IAdminAuditService audit,
        ILogger<SplitAutoAttachService> logger)
    {
        _db = db;
        _attach = attach;
        _audit = audit;
        _logger = logger;
    }

    public async Task<SplitAutoOutcome> AttachAfterImportAsync(int orgCompId, Guid jobId, CancellationToken ct = default)
    {
        var days = await _db.Competitions.AsNoTracking()
            .Where(c => c.OrgCompId == orgCompId)
            .Select(c => new { c.Id, c.EventId })
            .ToListAsync(ct);
        if (days.Count == 0)
            return new SplitAutoOutcome(SplitAutoKind.Failed, 0, 0,
                $"дней с compID {orgCompId} в базе нет — доклеивать не к чему", []);

        // Доклейка сама идёт по ВСЕМ дням события (§6-2 плана) — один прогон на событие, иначе
        // одно и то же качалось бы с loglig столько раз, сколько дней.
        var runs = days.GroupBy(d => d.EventId ?? -d.Id)
            .Select(g => (Head: g.Min(d => d.Id), Ids: g.Select(d => d.Id).ToList()))
            .OrderBy(r => r.Head).ToList();

        var kinds = new List<SplitAutoKind>();
        var touched = new List<int>();
        var messages = new List<string>();
        int swims = 0, legs = 0;
        foreach (var (head, ids) in runs)
        {
            SplitAttachResult result;
            try
            {
                result = await _attach.AttachAsync(head, logligId: null, apply: true, ct);
            }
            catch (Exception ex)
            {
                // Таймаут (токен с потолком) и остановка хоста — тоже сюда: строка аудита
                // «не удалось» лучше, чем молчание. Запись аудита — без токена, он уже отменён.
                _logger.LogWarning(ex, "Автодоклейка промежуточных: сорвалась (competitionId={Id}, job {JobId})", head, jobId);
                result = SplitAttachResult.Failed(ex is OperationCanceledException
                    ? "прервано: доклейка шла дольше потолка или процесс останавливался"
                    : $"не удалось: {ex.Message}");
            }

            var kind = SplitAutoAttachRules.Classify(result);
            kinds.Add(kind);
            messages.Add(result.Message);
            swims += result.Report.SwimWrites;
            legs += result.Report.LegWrites;

            // Сбой отчёта по дням не даёт — пишем дням этого compID, которые были в прогоне.
            var dayRows = result.Days.Count > 0
                ? result.Days.Select(d => (d.CompetitionId, d.SwimWrites, d.LegWrites)).ToList()
                : ids.Select(id => (id, 0, 0)).ToList();
            foreach (var (dayId, daySwims, dayLegs) in dayRows)
            {
                touched.Add(dayId);
                var dayKind = SplitAutoAttachRules.DayKind(kind, daySwims + dayLegs);
                await _audit.LogAsync(SplitAutoAttachRules.AuditAction, "Competition", dayId.ToString(),
                    SplitAutoAttachRules.AuditSummary(dayId, dayKind, daySwims, dayLegs),
                    new
                    {
                        Trigger = SplitAutoAttachRules.Trigger,
                        Kind = SplitAutoAttachRules.Code(dayKind),
                        SwimWrites = daySwims,
                        LegWrites = dayLegs,
                        JobId = jobId,
                        OrgCompId = orgCompId,
                        result.LogligId,
                        result.Message,
                    }, CancellationToken.None);
            }
        }

        return new SplitAutoOutcome(SplitAutoAttachRules.Combine(kinds), swims, legs,
            string.Join(" | ", messages), touched);
    }
}
