using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Боевые прогоны доклейки промежуточных с итогом в аудите: сама после импорта (решение Влада
/// 23.11.2026) и кнопкой «Промежуточные». Правило доклейки — в <see cref="ISplitAttachService"/>
/// (тот же, что у сухого прогона и CLI); здесь только: какие дни, исход и строка аудита КАЖДОМУ
/// дню прогона — всегда, и при «нечего», и при сбое: по последней строке шаг 9 чек-листа
/// показывает итог дня, поэтому повтор кнопкой обязан её перекрыть.
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

        // Только дни ЭТОГО compID (onlyDays): в окружных чемпионатах дни одного события — разные
        // compID и разные турниры loglig (e1, e7, e10, e12); прогон по всему событию взял бы
        // LogligId соседнего дня. Один прогон на событие — дни внутри него качаются за один раз.
        var runs = days.GroupBy(d => d.EventId ?? -d.Id)
            .Select(g => (Head: g.Min(d => d.Id), Ids: g.Select(d => d.Id).ToList()))
            .OrderBy(r => r.Head).ToList();

        var kinds = new List<SplitAutoKind>();
        var touched = new List<int>();
        var messages = new List<string>();
        int swims = 0, legs = 0;
        foreach (var (head, ids) in runs)
        {
            var (run, kind) = await RunAsync(head, logligId: null, ids, ct);
            var result = run.Result;
            kinds.Add(kind);
            messages.Add(result.Message);
            swims += result.Report.SwimWrites;
            legs += result.Report.LegWrites;
            touched.AddRange(await RecordAsync(run, kind, ids, auto: true, new { JobId = jobId, OrgCompId = orgCompId }));
        }

        return new SplitAutoOutcome(SplitAutoAttachRules.Combine(kinds), swims, legs,
            string.Join(" | ", messages), touched);
    }

    public async Task<SplitButtonRun> AttachByButtonAsync(int competitionId, int? logligId, CancellationToken ct = default)
    {
        var (run, kind) = await RunAsync(competitionId, logligId, onlyDays: null, ct);
        // Сбой отчёта по дням не даёт — итог пишем всем дням события, иначе у соседнего дня
        // остался бы прежний (например «не удалось») итог.
        var comp = await _db.Competitions.AsNoTracking().Where(c => c.Id == competitionId)
            .Select(c => new { c.Id, c.EventId }).FirstOrDefaultAsync(CancellationToken.None);
        var eventDays = comp is null ? []
            : comp.EventId is int ev
                ? await _db.Competitions.AsNoTracking().Where(c => c.EventId == ev).Select(c => c.Id).ToListAsync(CancellationToken.None)
                : [comp.Id];
        await RecordAsync(run, kind, eventDays, auto: false, extra: null);
        return new SplitButtonRun(run.Result, kind, run.Threw);
    }

    /// <summary>
    /// Боевой прогон, переживающий любой сбой. Запись доклейки — одна транзакция, но отменённая
    /// посреди записи (потолок, остановка, ошибка базы) оставляет изменённые ноги и строки в
    /// трекере общего контекста — и следующий <c>SaveChanges</c> (аудит!) записал бы их частично.
    /// Поэтому при сбое трекер чистится до любой другой записи.
    /// </summary>
    private async Task<(RunResult Result, SplitAutoKind Kind)> RunAsync(
        int competitionId, int? logligId, IReadOnlyCollection<int>? onlyDays, CancellationToken ct)
    {
        try
        {
            var result = await _attach.AttachAsync(competitionId, logligId, apply: true, ct, onlyDays);
            return (new RunResult(result, Threw: false), SplitAutoAttachRules.Classify(result));
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Доклейка промежуточных: сорвалась (competitionId={Id})", competitionId);
            var failed = SplitAttachResult.Failed(FailureText(ex, ct));
            return (new RunResult(failed, Threw: true), SplitAutoKind.Failed);
        }
    }

    /// <summary>
    /// Таймаут ОДНОГО запроса к loglig (HttpClient отменяет сам, наш токен цел) — не то же, что
    /// потолок всей доклейки или остановка процесса (отменён наш токен).
    /// </summary>
    internal static string FailureText(Exception ex, CancellationToken ct) => ex switch
    {
        OperationCanceledException when ct.IsCancellationRequested =>
            "прервано: доклейка шла дольше своего потолка, процесс останавливался или запрос закрыли",
        TaskCanceledException or TimeoutException => $"loglig не ответил вовремя (таймаут запроса): {ex.Message}",
        _ => $"не удалось: {ex.Message}",
    };

    /// <summary>Строка аудита каждому дню прогона; возвращает дни, которым она записана.</summary>
    private async Task<List<int>> RecordAsync(RunResult run, SplitAutoKind kind, IReadOnlyList<int> fallbackDays,
        bool auto, object? extra)
    {
        var r = run.Result;
        var rows = r.Days.Count > 0
            ? r.Days.Select(d => (d.CompetitionId, d.SwimWrites, d.LegWrites)).ToList()
            : fallbackDays.Select(id => (id, 0, 0)).ToList();
        foreach (var (dayId, daySwims, dayLegs) in rows)
        {
            var dayKind = SplitAutoAttachRules.DayKind(kind, daySwims + dayLegs);
            // Без токена: прогон мог кончиться отменой, а итог записать нужно всё равно.
            await _audit.LogAsync(SplitAutoAttachRules.AuditAction, "Competition", dayId.ToString(),
                SplitAutoAttachRules.AuditSummary(dayId, dayKind, daySwims, dayLegs, auto),
                new
                {
                    Trigger = auto ? SplitAutoAttachRules.Trigger : SplitAutoAttachRules.ManualTrigger,
                    Kind = SplitAutoAttachRules.Code(dayKind),
                    SwimWrites = daySwims,
                    LegWrites = dayLegs,
                    r.LogligId,
                    r.Message,
                    Extra = extra,
                }, CancellationToken.None);
        }
        return rows.Select(x => x.Item1).ToList();
    }

    /// <summary>Итог прогона + бросил ли он (кнопке это ok:false).</summary>
    private sealed record RunResult(SplitAttachResult Result, bool Threw);
}
