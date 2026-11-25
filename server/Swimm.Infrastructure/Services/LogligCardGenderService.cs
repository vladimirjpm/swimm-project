using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Замок «по карточкам ради пола ходит один» — синглтон процесса. Без него второе нажатие кнопки
/// (или кнопка поверх шага импорта) гонялось бы с первым за строку <c>Sys_SwimmerLogligClubs</c>
/// (FindAsync → Add) и дублировало бы аудит.
/// </summary>
public sealed class LogligCardGenderGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}

/// <summary>
/// Р125/Р126: пол пловца — с карточки loglig (см. <see cref="ILogligCardGenderService"/>).
/// Вердикты — чистые <see cref="LogligCardGenderRule"/> и <see cref="GenderImpactRules"/>; здесь
/// выборка, сеть, суточный ответ и запись.
///
/// • Сеть — только по int loglig-id, последовательно, не чаще раза в <see cref="DefaultRequestGap"/>,
///   не больше потолка запросов на прогон; вне транзакции импорта (шаг идёт после него, в своём scope).
/// • Свежий ответ из <c>Sys_SwimmerLogligClubs</c> берётся без сети; каждый удачный визит туда же
///   и пишется — вместе с клубом (Р112) и флагом «заплыв пловца на карточке найден».
/// • Тот же человек — имя и год (правило) И заплыв: совпавший результат на карточке
///   (<see cref="ILogligMatchService"/>) либо строка пловца с этим игроком loglig (Р122).
/// • Серьёзные последствия (<see cref="IGenderImpactService"/>) или неподтверждённая личность —
///   пол не пишется, случай ждёт человека.
/// • Запись условная (<see cref="IDataCheckRunner.SetSwimmerGenderIfEmptyAsync"/>). Сбой на одном
///   пловце остальных не срывает: трекер чистится, счётчик <c>DbFailed</c>.
/// </summary>
public sealed class LogligCardGenderService(
    SwimmDbContext db,
    ILogligClient loglig,
    IDataCheckRunner checks,
    IAdminAuditService audit,
    IGenderImpactService impact,
    LogligGenderReviewFinder reviews,
    ILogligMatchService match,
    ILogger<LogligCardGenderService> logger,
    LogligCardGenderGate? gate = null,
    ICompetitionRecalculationService? recalculation = null,
    TimeSpan? requestGap = null) : ILogligCardGenderService
{
    /// <summary>Пауза между запросами к loglig — как у сверки клуба (Р98).</summary>
    public static readonly TimeSpan DefaultRequestGap = SwimmerClubLogligCheckService.DefaultRequestGap;

    private readonly TimeSpan _gap = requestGap ?? DefaultRequestGap;
    private readonly SemaphoreSlim _gate = (gate ?? new LogligCardGenderGate()).Semaphore;
    private DateTime? _lastRequestAt;

    public async Task<LogligCardGenderReport?> FillFromFindingsAsync(CancellationToken ct = default)
    {
        // Кнопка не ждёт: идёт другой прогон — сразу отказ (409 у эндпоинта).
        if (!await _gate.WaitAsync(0, CancellationToken.None)) return null;
        try
        {
            var ids = await db.DataCheckFindings.AsNoTracking()
                .Where(f => f.Resolution == null && f.FixKind == DataCheckFixKinds.SwimmerGender && f.FixEntityId != null)
                .Select(f => f.FixEntityId!.Value)
                .Distinct()
                .ToListAsync(CancellationToken.None);
            return await RunAsync(db.Swimmers.Where(s => ids.Contains(s.Id)), "button",
                LogligCardGenderRule.MaxRequestsPerButton, ct);
        }
        finally { _gate.Release(); }
    }

    public Task<LogligCardGenderReport> FillAfterImportAsync(int orgCompId, CancellationToken ct = default) =>
        GatedAsync(() => RunAsync(ImportSwimmers(db.Results.Where(r => r.Competition!.OrgCompId == orgCompId)),
            "import", LogligCardGenderRule.MaxRequestsPerImport, ct, new { OrgCompId = orgCompId }), ct);

    public Task<LogligCardGenderReport> FillDayAfterImportAsync(int competitionId, CancellationToken ct = default) =>
        GatedAsync(() => RunAsync(ImportSwimmers(db.Results.Where(r => r.CompetitionId == competitionId)),
            "import", LogligCardGenderRule.MaxRequestsPerImport, ct, new { CompetitionId = competitionId }), ct);

    /// <summary>Пловцы импорта — подзапросом по id: переводится в SQL одним IN.</summary>
    internal IQueryable<Swimmer> ImportSwimmers(IQueryable<ResultRecord> results) =>
        db.Swimmers.Where(s => results.Select(r => r.SwimmerId).Contains(s.Id));

    /// <summary>Шаг импорта ждёт своей очереди (кнопка идёт не дольше своего потолка).</summary>
    private async Task<LogligCardGenderReport> GatedAsync(Func<Task<LogligCardGenderReport>> run, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await run(); }
        finally { _gate.Release(); }
    }

    private sealed class Tally
    {
        public int Set, Rows, NoGender, Failed, Mismatch, NotVerified, OverLimit, Requests, FromCache, Review, DbFailed, Reached,
            ReviewIdentity, ReviewImpact;
        public readonly List<string> Notes = [];
    }

    private async Task<LogligCardGenderReport> RunAsync(
        IQueryable<Swimmer> scope, string trigger, int maxRequests, CancellationToken ct, object? extra = null)
    {
        // Выборка и общий ответ — без токена прогона: отмена по времени должна дать итог, а не исключение.
        var all = await GenderCandidate.From(scope).ToListAsync(CancellationToken.None);
        var alreadyKnown = all.Count(s => LogligCardGenderRule.GenderKnown(s.Gender));
        var swimmers = all.Where(s => !LogligCardGenderRule.GenderKnown(s.Gender)).OrderBy(s => s.Id).ToList();
        var shared = await SwimmerLogligClubStore.LoadAsync(db, swimmers.Select(s => s.Id).ToArray(), CancellationToken.None);

        var t = new Tally();
        var interrupted = false;
        foreach (var s in swimmers)
        {
            if (ct.IsCancellationRequested) { interrupted = true; break; }
            try
            {
                await ProcessAsync(s, shared.GetValueOrDefault(s.Id), trigger, maxRequests, extra, t, ct);
                t.Reached++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                interrupted = true;
                break;
            }
            catch (Exception ex)
            {
                // Сбой базы на одном пловце: недописанное из трекера выбрасываем, идём дальше.
                db.ChangeTracker.Clear();
                t.DbFailed++;
                t.Reached++;
                t.Notes.Add($"#{s.Id} {s.Name}: сбой записи — {ex.Message}");
                logger.LogWarning(ex, "Пол с карточек loglig: сбой на пловце {SwimmerId}", s.Id);
            }
        }

        var message = LogligCardGenderRule.Summary(swimmers.Count, t.Set, t.Rows, t.NoGender, t.Failed, t.Mismatch,
            t.NotVerified, t.OverLimit, t.Requests, t.FromCache, t.Review, t.DbFailed, interrupted ? t.Reached : -1,
            t.ReviewIdentity, t.ReviewImpact);
        if (t.Requests > 0 || t.Set > 0 || t.Review > 0)
            logger.LogInformation("Пол с карточек loglig ({Trigger}): {Message}", trigger, message);
        return new LogligCardGenderReport(swimmers.Count, t.Set, t.Rows, t.NoGender, t.Failed, t.Mismatch, t.NotVerified,
            t.OverLimit, t.Requests, t.FromCache, alreadyKnown, message, t.Notes, t.Review, t.DbFailed, interrupted, t.Reached,
            t.ReviewIdentity, t.ReviewImpact);
    }

    private async Task ProcessAsync(GenderCandidate s, StoredLogligClub? answer, string trigger, int maxRequests, object? extra,
        Tally t, CancellationToken ct)
    {
        // (б) без подтверждённого id на loglig не ходим вовсе.
        if (LogligCardGenderRule.Decide(s.Subject, null).Verdict == CardGenderVerdict.NotVerified)
        {
            t.NotVerified++;
            return;
        }

        var lid = s.LogligId!.Value;
        var rowPlayer = await reviews.RowPlayerMatchedAsync(s.Id, lid, ct);
        var cached = LogligCardGenderRule.CacheDecides(DateTime.UtcNow, answer, lid, rowPlayer);
        if (cached) t.FromCache++;
        else
        {
            if (t.Requests >= maxRequests)
            {
                t.OverLimit++;
                return;
            }

            await ThrottleAsync(ct);
            var card = await loglig.GetPlayerCardAsync(lid, ct: ct);
            t.Requests++;
            if (card is null)
            {
                t.Failed++;
                t.Notes.Add($"#{s.Id} {s.Name}: карточка loglig #{lid} не открылась");
                return;
            }

            var at = DateTime.UtcNow;
            var club = card.ClubName?.Trim();
            var matched = await CardResultsMatchedAsync(s, card, ct);
            await SwimmerLogligClubStore.UpsertAsync(db, s.Id, lid, club, at, ct, card, matched);
            await db.SaveChangesAsync(ct);
            answer = new StoredLogligClub(lid, club, at, card.FullName, card.BirthYear,
                LogligCardGenderRule.ToSwimmerGender(card.Gender), matched);
        }

        var (verdict, gender) = LogligCardGenderRule.Decide(s.Subject, GenderCandidate.Answer(answer!));
        switch (verdict)
        {
            case CardGenderVerdict.CardHasNoGender:
                t.NoGender++;
                t.Notes.Add($"#{s.Id} {s.Name}: на карточке loglig #{lid} пол не указан");
                return;

            case CardGenderVerdict.NameMismatch or CardGenderVerdict.YearMismatch:
                t.Mismatch++;
                t.Notes.Add($"#{s.Id} {s.Name} ({s.BirthYear}): карточка loglig #{lid} — "
                            + $"«{answer!.CardName}» ({answer.CardBirthYear?.ToString() ?? "год не указан"}), "
                            + (verdict == CardGenderVerdict.YearMismatch ? "год не совпал" : "имя не совпало")
                            + " — пол не взят");
                return;

            case CardGenderVerdict.Write:
                break;

            default:
                return;
        }

        // Р126: личность подтверждена заплывом и серьёзных последствий нет — иначе человеку.
        var reasons = LogligCardGenderRule.ReviewReasons(answer!.ResultsMatched == true, rowPlayer,
            await impact.AssessAsync(s.Id, gender!, ct));
        if (reasons.Count > 0)
        {
            t.Review++;
            if (reasons.Any(r => r.Code == GenderImpactRules.IdentityUnconfirmed)) t.ReviewIdentity++;
            if (reasons.Any(r => r.Code != GenderImpactRules.IdentityUnconfirmed)) t.ReviewImpact++;
            t.Notes.Add($"#{s.Id} {s.Name} ({s.BirthYear}) — ждёт ручной проверки: " + string.Join("; ", reasons.Select(r => r.Text)));
            return;
        }

        // Условная запись: пол, который за это время проставил импорт или человек, старше.
        if (await checks.SetSwimmerGenderIfEmptyAsync(s.Id, gender!, ct) is not int written) return;
        t.Set++;
        t.Rows += written;
        await audit.LogAsync(LogligCardGenderRule.AuditAction, "Swimmer", s.Id.ToString(),
            $"Пол '{gender}' пловцу #{s.Id} «{s.Name}» — с карточки loglig #{lid} "
            + $"({LogligCardGenderRule.Source}); строк результата дописано — {written}",
            new
            {
                LogligCardGenderRule.Source,
                LogligPlayerId = lid,
                Gender = gender,
                Rows = written,
                Trigger = trigger,
                answer.CardName,
                answer.CardBirthYear,
                CardResultsMatched = answer.ResultsMatched,
                RowPlayerMatched = rowPlayer,
                FromCache = cached,
                Extra = extra,
            }, CancellationToken.None);
    }

    /// <summary>
    /// Итог прошлого нажатия — строка аудита <see cref="LogligCardGenderRule.ButtonAuditAction"/>
    /// (её пишет контроллер в любом исходе). Список «кого смотреть руками» — из её деталей.
    /// </summary>
    public async Task<LogligGenderButtonRun?> LastButtonRunAsync(CancellationToken ct = default)
    {
        var row = await db.AdminAudits.AsNoTracking()
            .Where(a => a.Action == LogligCardGenderRule.ButtonAuditAction)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Select(a => new { a.CreatedAt, a.ActorName, a.Summary, a.DetailsJson })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var notes = new List<string>();
        try
        {
            if (row.DetailsJson is not null
                && System.Text.Json.JsonDocument.Parse(row.DetailsJson).RootElement is { ValueKind: System.Text.Json.JsonValueKind.Object } root
                && root.TryGetProperty("Notes", out var list) && list.ValueKind == System.Text.Json.JsonValueKind.Array)
                notes.AddRange(list.EnumerateArray().Select(n => n.GetString()).OfType<string>());
        }
        catch (System.Text.Json.JsonException) { /* старая строка без списка — показываем только итог */ }

        return new LogligGenderButtonRun(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc), row.ActorName, row.Summary, notes);
    }

    public async Task<LogligGenderConfirmResult?> ConfirmFindingAsync(int findingId, CancellationToken ct = default)
    {
        var f = await db.DataCheckFindings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == findingId, ct);
        return f is { FixKind: DataCheckFixKinds.SwimmerGenderLogligReview, FixEntityId: int swimmerId }
            ? await ConfirmAsync(swimmerId, ct)
            : null;
    }

    public async Task<LogligGenderConfirmResult?> ConfirmAsync(int swimmerId, CancellationToken ct = default)
    {
        var s = await GenderCandidate.From(db.Swimmers.Where(x => x.Id == swimmerId)).FirstOrDefaultAsync(ct);
        if (s is null) return null;
        var answer = (await SwimmerLogligClubStore.LoadAsync(db, [swimmerId], ct)).GetValueOrDefault(swimmerId);
        if (answer is null || answer.LogligId != s.LogligId || answer.CardName is null) return null;

        // Человек подтверждает последствия и личность, но не правило (а)–(г): пол всё ещё должен
        // быть пуст, id — подтверждён, имя, год и пол карточки — на месте.
        var (verdict, gender) = LogligCardGenderRule.Decide(s.Subject, GenderCandidate.Answer(answer));
        if (verdict != CardGenderVerdict.Write) return null;

        var rowPlayer = await reviews.RowPlayerMatchedAsync(swimmerId, answer.LogligId, ct);
        var reasons = LogligCardGenderRule.ReviewReasons(answer.ResultsMatched == true, rowPlayer,
            await impact.AssessAsync(swimmerId, gender!, ct));
        var competitions = await db.Results.AsNoTracking()
            .Where(r => r.SwimmerId == swimmerId && r.RelayId == null
                        && (r.Gender == null || r.Gender == "" || r.Gender == "none"))
            .Select(r => new { r.CompetitionId, r.Competition!.EventId })
            .Distinct().ToListAsync(ct);

        if (await checks.SetSwimmerGenderIfEmptyAsync(swimmerId, gender!, ct) is not int written) return null;

        await audit.LogAsync(LogligCardGenderRule.ConfirmAuditAction, "Swimmer", swimmerId.ToString(),
            $"Пол '{gender}' пловцу #{swimmerId} «{s.Name}» — с карточки loglig #{answer.LogligId}, "
            + $"подтвердил человек (последствий: {reasons.Count}); строк результата дописано — {written}",
            new
            {
                LogligCardGenderRule.Source,
                ConfirmedByHuman = true,
                LogligPlayerId = answer.LogligId,
                Gender = gender,
                Rows = written,
                answer.CardName,
                answer.CardBirthYear,
                Consequences = reasons.Select(r => new { r.Code, r.Text }),
            }, CancellationToken.None);

        // Последствия приняты осознанно — материализуем их сразу (объединённые места, пара-очки,
        // клубный зачёт), а не «при следующем пересчёте». Один пересчёт на событие.
        var recalculated = 0;
        if (recalculation is not null)
            foreach (var scope in competitions.GroupBy(c => c.EventId ?? -c.CompetitionId))
            {
                try
                {
                    await recalculation.RecalculateCompetitionAsync(scope.First().CompetitionId, CancellationToken.None);
                    recalculated++;
                }
                catch (Exception ex)
                {
                    db.ChangeTracker.Clear();
                    logger.LogWarning(ex, "Пол подтверждён, пересчёт соревнования {CompetitionId} сорвался", scope.First().CompetitionId);
                }
            }

        return new LogligGenderConfirmResult(gender!, written, recalculated);
    }

    /// <summary>На карточке найден хоть один наш заплыв пловца (дата + дистанция + стиль + время).</summary>
    private async Task<bool> CardResultsMatchedAsync(GenderCandidate s, LogligPlayerCard card, CancellationToken ct)
    {
        if (card.Results.Count == 0) return false;
        var local = await db.Results.AsNoTracking()
            .Where(r => r.SwimmerId == s.Id && r.RelayId == null)
            .Select(r => new LocalResultKey(r.CompetitionDate, r.Distance, r.Style.Name, r.TimeMillisecond))
            .ToListAsync(ct);
        return match.Match(card, s.BirthYear, null, local).MatchedResultCount > 0;
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        if (_lastRequestAt is DateTime last)
        {
            var wait = _gap - (DateTime.UtcNow - last);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        }
        _lastRequestAt = DateTime.UtcNow;
    }
}
