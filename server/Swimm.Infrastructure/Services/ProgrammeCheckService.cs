using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IProgrammeCheckService"/>
/// <remarks>
/// Программу читает тот же <see cref="IDisciplineDateSource"/>, что и деление, а план деления
/// берётся у <see cref="ICompetitionDaySplitService"/> (dry-run): карточка и кнопка видят ровно то,
/// что потом запишет деление (правило 1 data-integrity). Алгоритм деления здесь не повторяется.
/// Запросов к loglig на клик: программа (1) + dry-run деления (ещё 1, только при «дни склеены»)
/// или поиск соседей (заголовок привязанного + 6 соседей = 7, всего 8).
/// </remarks>
public sealed class ProgrammeCheckService(
    SwimmDbContext db,
    IDisciplineDateSource dateSource,
    ICompetitionDaySplitService splitter,
    ICompetitionDiscoveryProvider loglig,
    IAdminAuditService audit,
    ILogger<ProgrammeCheckService> logger) : IProgrammeCheckService
{
    private const string DateFormat = "dd/MM/yyyy";

    /// <summary>Общий предел на весь поиск соседей: кто не успел — «не ответил».</summary>
    internal static readonly TimeSpan NeighbourSearchBudget = TimeSpan.FromSeconds(45);

    public async Task<ProgrammeCheckVerdict> CheckAsync(int discoveryId, CancellationToken ct = default) =>
        (await CheckCoreAsync(discoveryId, ct)).Verdict;

    public async Task<ProgrammeSplitOutcome> SplitAsync(int discoveryId, string? planFingerprint, CancellationToken ct = default)
    {
        var (verdict, logligId) = await CheckCoreAsync(discoveryId, ct);
        if (verdict.SplitCompetitionId is not int competitionId)
            return ProgrammeSplitOutcome.Refused(verdict.SplitBlocked ?? $"делить нечего: {verdict.Headline}");
        // План, который видел админ, против нового dry-run: строки дня могли переехать (импорт, правка).
        if (!string.Equals(verdict.PlanFingerprint, planFingerprint, StringComparison.Ordinal))
            return ProgrammeSplitOutcome.Refused("план изменился — проверьте ещё раз");

        // С этого места — без токена запроса: закрытая вкладка после commit оставила бы дни без
        // пересчёта и без аудита. Сервис деления сам заново строит план и откажет целиком, если
        // что-то изменилось (всё или ничего; под advisory-блокировкой).
        CompetitionDaySplitReport report;
        try
        {
            report = await splitter.SplitAsync(competitionId, logligId, apply: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Пересчёт идёт ПОСЛЕ commit: упал он — деление уже в базе, и аудит обязан это записать.
            if (await AppliedDaysAsync(competitionId) is not { } applied)
            {
                logger.LogWarning(ex, "Деление #{CompetitionId} не выполнено", competitionId);
                return ProgrammeSplitOutcome.Refused($"деление не выполнено: {ex.Message}");
            }
            logger.LogError(ex, "Деление #{CompetitionId} записано, но пересчёт не завершён", competitionId);
            await LogAuditAsync(applied, discoveryId, ex.Message);
            return new ProgrammeSplitOutcome(null, applied, ex.Message);
        }
        if (!report.Applied)
            return ProgrammeSplitOutcome.Refused(report.Refusal ?? "деление не применено");

        await LogAuditAsync(report, discoveryId, null);
        return new ProgrammeSplitOutcome(null, report);
    }

    /// <summary>Аудит <c>competition.split-days</c> — тот же вид, что у CLI, плюс откуда нажато.</summary>
    private Task LogAuditAsync(CompetitionDaySplitReport r, int discoveryId, string? recalcError) =>
        audit.LogAsync("competition.split-days", "Competition", r.CompetitionId.ToString(CultureInfo.InvariantCulture),
            $"Многодневка разделена по дням (loglig {r.LogligId}): событие #{r.EventId}, "
            + string.Join("; ", r.Days.Select(d =>
                $"день {d.DayNumber} {d.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} #{d.CompetitionId} — {d.Rows} строк"))
            + $" — кнопка карточки /Admin/Competitions (discovery #{discoveryId})"
            + (recalcError != null ? $". ПЕРЕСЧЁТ НЕ ЗАВЕРШЁН: {recalcError}" : ""),
            ct: CancellationToken.None);

    /// <summary>
    /// Деление записано? До деления у соревнования один день (иначе вердикт не дал бы кнопку) —
    /// значит, событие с 2+ днями означает, что commit прошёл. Дни — из базы, для аудита.
    /// </summary>
    private async Task<CompetitionDaySplitReport?> AppliedDaysAsync(int competitionId)
    {
        var comp = await db.Competitions.AsNoTracking().Where(c => c.Id == competitionId)
            .Select(c => new { c.Name, c.EventId }).FirstOrDefaultAsync(CancellationToken.None);
        if (comp?.EventId is not int eventId) return null;
        var days = await db.Competitions.AsNoTracking().Where(c => c.EventId == eventId)
            .Select(c => new { c.Id, c.Date, c.DayNumber, Rows = db.Results.Count(r => r.CompetitionId == c.Id) })
            .ToListAsync(CancellationToken.None);
        if (days.Count < 2) return null;
        return new CompetitionDaySplitReport(competitionId, comp.Name, null,
            days.OrderBy(d => d.DayNumber).Select(d => new CompetitionDaySplitDay(
                ParseDate(d.Date) ?? default, d.DayNumber ?? 0, d.Id, d.Id == competitionId, d.Rows, 0)).ToList(),
            new Dictionary<string, int>(), null, true, eventId);
    }

    private async Task<(ProgrammeCheckVerdict Verdict, int? LogligId)> CheckCoreAsync(int discoveryId, CancellationToken ct)
    {
        var row = await db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.Id == discoveryId)
            .Select(d => new { d.OrgCompId, d.LogligId, d.DateStart, d.DateEnd })
            .FirstOrDefaultAsync(ct);
        if (row == null)
            return (new ProgrammeCheckVerdict(ProgrammeVerdictKind.None, $"Строка входящих #{discoveryId} не найдена.", [], null, null, null, false), null);

        // Дни compID без чужого штампа — тот же круг, что у CollapsedProtocolGuard (Р118): событие из
        // окружных протоколов держит под одним EventId дни разных compID.
        var days = (await CompetitionIdentity.ResolveDaysAsync(db, row.OrgCompId, ct))
            .Where(d => d.OrgCompId is null || d.OrgCompId == row.OrgCompId)
            .ToList();
        var dbDays = days.Select(c => ParseDate(c.Date)).OfType<DateOnly>().Distinct().ToList();

        IReadOnlyList<DateOnly>? programme = null;
        string? error = null;
        CompetitionDaySplitReport? plan = null;
        if (row.LogligId is int loglig)
        {
            try
            {
                programme = (await dateSource.FetchAsync(loglig, ct)).Days;
                // Dry-run тоже ходит в loglig — его сбой такой же «loglig не ответил», а не 500.
                if (days.Count == 1 && ProgrammeCheckRules.NeedsSplitPlan(dbDays, programme))
                    plan = await splitter.SplitAsync(days[0].Id, row.LogligId, apply: false, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Программа loglig {LogligId} для discovery #{DiscoveryId} не получена", loglig, discoveryId);
                error = ex.Message;
                plan = null;
            }
        }

        var siteStart = DateOnly.FromDateTime(row.DateStart);
        var siteEnd = DateOnly.FromDateTime(row.DateEnd);
        string? boundTitle = null;
        List<LogligCompetitionSummaryDto>? neighbours = null;
        var failed = 0;
        if (error == null && row.LogligId is int bound
            && ProgrammeCheckRules.NeedsNeighbourSearch(siteStart, siteEnd, dbDays, programme))
            (boundTitle, neighbours, failed) = await SearchNeighboursAsync(bound, discoveryId, ct);

        var verdict = ProgrammeCheckRules.Verdict(new ProgrammeCheckInput(
            siteStart, siteEnd, dbDays, row.LogligId, programme, error, plan, boundTitle, neighbours, failed));
        return (verdict, row.LogligId);
    }

    /// <summary>
    /// Р123: заголовок привязанного соревнования + соседи ±3 (6) = 7 запросов, по одному, с обычной
    /// паузой провайдера (вежливый режим), под общим пределом <see cref="NeighbourSearchBudget"/>.
    /// Пустой номер (404) — не ошибка; сбой и не успевшие к пределу — «не ответили».
    /// </summary>
    private async Task<(string? BoundTitle, List<LogligCompetitionSummaryDto>? Neighbours, int Failed)> SearchNeighboursAsync(
        int bound, int discoveryId, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(NeighbourSearchBudget);

        string? boundTitle;
        try
        {
            boundTitle = (await loglig.FetchCompetitionSummaryAsync(bound, budget.Token))?.Title;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Заголовок loglig {LogligId} (discovery #{DiscoveryId}) не получен", bound, discoveryId);
            return (null, null, 0);
        }
        if (boundTitle == null) return (null, [], 0);

        var found = new List<LogligCompetitionSummaryDto>();
        var failed = 0;
        foreach (var id in ProgrammeCheckRules.NeighbourIds(bound))
        {
            if (budget.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                failed++; // предел вышел — оставшиеся не спрошены
                continue;
            }
            try
            {
                if (await loglig.FetchCompetitionSummaryAsync(id, budget.Token) is { } summary) found.Add(summary);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // номера нет — законный ответ
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogInformation(ex, "Сосед loglig {LogligId} (discovery #{DiscoveryId}) не прочитан", id, discoveryId);
                failed++;
            }
        }
        return (boundTitle, found, failed);
    }

    private static DateOnly? ParseDate(string? date) =>
        DateOnly.TryParseExact(date?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
