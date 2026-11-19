using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Задача results-auto, режим report (П5, docs/plans/prod-auto-pull-plan.md §4.4–4.5). За тик:
/// выбрать одну строку (<see cref="ResultsAutoRules.Pick"/>) → гейты без сети → тот же путь, что
/// строка пакетного затягивания (<see cref="IBulkPullService.PullOneAsync"/>: превью «Затянуть» +
/// регламент + классификатор) → гейты §4.4 (<see cref="ResultsAutoRules.Decide"/>) → вердикт и
/// причина в полях строки «Входящих».
///
/// ИМПОРТА НЕТ: превью снимается из кэша сразу после решения, в Competitions / Results не пишется
/// ничего. Режим import пока ведёт себя как report (импорт — П6) и говорит об этом в итоге.
///
/// Превью само по себе пишет только в строку «Входящих» (пометка «протокола нет», языки PDF) —
/// так же, как одиночная кнопка «Затянуть».
/// </summary>
public sealed class ResultsAutoService(
    SwimmDbContext db,
    ICompetitionDiscoveryService discovery,
    IBulkPullService bulkPull,
    IDiscoveryPreviewService previews,
    IVenuePoolService venuePools,
    ISettingsService settings,
    ILogger<ResultsAutoService> logger,
    TimeProvider? time = null) : IResultsAutoService
{
    /// <summary>
    /// Потолок на одну строку (два PDF, проба зачёта, регламент, превью рекордов). Обычно — минута-две;
    /// зависший loglig не должен держать заявку прогона часами (§6: публичный процесс).
    /// </summary>
    public static readonly TimeSpan RowTimeout = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<ResultsAutoTickReport> RunTickAsync(CancellationToken ct = default) => RunOnceAsync(new(), ct);

    public async Task<ResultsAutoTickReport> RunOnceAsync(ResultsAutoRunOptions options, CancellationToken ct = default)
    {
        var mode = ResultsAutoModes.Parse(options.Mode ?? settings.Get(ResultsAutoModes.SettingKey)?.Value);
        if (mode == ResultsAutoModes.Off)
            return new ResultsAutoTickReport(mode, 0, 0, null);

        var now = _time.GetUtcNow().UtcDateTime;
        var rows = await discovery.GetAllAsync(ct);
        var state = await db.DiscoveredCompetitions.AsNoTracking()
            .Select(d => new { d.Id, d.Discipline, d.EmptySourceAt, d.EmptySourceBy, d.MissingSince, d.AutoPullCheckedAt, d.AutoPullVerdict })
            .ToDictionaryAsync(d => d.Id, ct);

        var candidates = rows
            .Where(r => state.ContainsKey(r.Id))
            .Select(r =>
            {
                var s = state[r.Id];
                return new ResultsAutoCandidate(
                    r.Id, r.Name, r.Status, s.Discipline, r.LogligId, r.DateStart, r.DateEnd,
                    AlreadyInDb: r.MatchedCompetitionId is not null,
                    // «Нет протокола» от разбора («auto») перепроверяем — файл выкладывают позже;
                    // поставленную человеком пометку — нет.
                    ManualEmpty: s.EmptySourceAt is not null && s.EmptySourceBy != "auto",
                    s.MissingSince, s.AutoPullCheckedAt, s.AutoPullVerdict);
            })
            .ToList();

        var pick = ResultsAutoRules.Pick(candidates, now);
        if (options.DiscoveredId is int forced)
            pick = pick with { Next = candidates.FirstOrDefault(c => c.Id == forced) };
        var importNotImplemented = mode == ResultsAutoModes.Import;
        if (pick.Next is not { } next)
            return new ResultsAutoTickReport(mode, pick.InWindow, pick.Due, null, importNotImplemented);

        var dto = rows.First(r => r.Id == next.Id);
        var outcome = await CheckRowAsync(dto, next, ct);
        await SaveVerdictAsync(next.Id, outcome, now, ct);

        logger.LogInformation("results-auto ({Mode}): строка {Id} «{Name}» → {Verdict}: {Reason}",
            mode, outcome.DiscoveredId, outcome.Name, outcome.Verdict, ResultsAutoRules.JoinReasons(outcome.Reasons));
        return new ResultsAutoTickReport(mode, pick.InWindow, pick.Due, outcome, importNotImplemented);
    }

    private async Task<ResultsAutoRowOutcome> CheckRowAsync(
        DiscoveredCompetitionDto row, ResultsAutoCandidate candidate, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();

        // Чемпионат и многодневка видны по имени и датам — PDF ради них не качаем.
        if (ResultsAutoRules.PreGate(candidate) is { } early)
            return Outcome(row, early, null, null, watch);

        using var rowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        rowCts.CancelAfter(RowTimeout);

        BulkPullRowPull pull;
        try
        {
            pull = await bulkPull.PullOneAsync(row, rowCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // остановка приложения — не вердикт
        }
        catch (Exception ex)
        {
            // Сеть, вёрстка, таймаут строки: «сбой», повтор через ResultsAutoRules.FailedRecheck.
            logger.LogWarning(ex, "results-auto: строка {Id} не затянулась", row.Id);
            var reason = ex is OperationCanceledException
                ? $"не уложилась в {RowTimeout.TotalMinutes:0} мин — loglig отвечает слишком долго"
                : $"сбой затягивания: {ex.Message.Split('\n', 2)[0].Trim()}";
            return Outcome(row, new ResultsAutoDecision(ResultsAutoVerdicts.Failed, [reason]), null, null, watch);
        }

        try
        {
            var preview = pull.Preview;
            CompetitionProgressSummary? progress = null;
            if (preview.Parsed is { } parsed)
            {
                try { progress = CompetitionProgress.EvaluateJson(parsed.ResultsJson); }
                catch (System.Text.Json.JsonException ex)
                {
                    logger.LogWarning(ex, "results-auto: разбор строки {Id} не читается как JSON", row.Id);
                }
            }

            var analysis = pull.Regulation?.Analysis;
            var directory = await venuePools.LoadAsync(ct);
            var input = new ResultsAutoGateInput(
                pull.Row.Verdict,
                pull.Row.Reasons,
                CompetitionAdminRepository.IsChampionship(row.Name, analysis?.IsChampionship ?? false),
                preview.Parsed?.Competitions.Count ?? 0,
                progress,
                analysis?.PoolType,
                directory.Resolve(row.Venue));

            return Outcome(row, ResultsAutoRules.Decide(input), pull, progress, watch);
        }
        finally
        {
            // report: импортировать некому — разбор (мегабайты JSON) не держим в памяти час.
            if (pull.Preview.PreviewId != Guid.Empty)
                previews.RemoveEntry(pull.Preview.PreviewId);
        }
    }

    private static ResultsAutoRowOutcome Outcome(
        DiscoveredCompetitionDto row, ResultsAutoDecision decision, BulkPullRowPull? pull,
        CompetitionProgressSummary? progress, Stopwatch watch) =>
        new(row.Id, row.OrgCompId, row.Name, row.DateEnd, row.Venue,
            decision.Verdict, decision.Reasons, decision.PoolType, decision.PoolTypeSource,
            pull?.Row.Verdict.ToString(),
            pull?.Row.ResultCount ?? 0,
            pull?.Row.DayCount ?? 0,
            progress?.Events.Count ?? 0,
            progress?.Events.Count(e => !e.Finished) ?? 0,
            watch.ElapsedMilliseconds);

    /// <summary>Вердикт в строку «Входящих». Строку могли удалить, пока шёл разбор, — тогда молча нечего писать.</summary>
    private async Task SaveVerdictAsync(int id, ResultsAutoRowOutcome outcome, DateTime now, CancellationToken ct)
    {
        var entity = await db.DiscoveredCompetitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (entity is null) return;
        entity.AutoPullVerdict = outcome.Verdict;
        entity.AutoPullBlockedReason = ResultsAutoRules.JoinReasons(outcome.Reasons);
        entity.AutoPullCheckedAt = now;
        entity.AutoPullPoolType = outcome.Verdict == ResultsAutoVerdicts.WouldTake ? outcome.PoolType : null;
        await db.SaveChangesAsync(ct);
    }
}
