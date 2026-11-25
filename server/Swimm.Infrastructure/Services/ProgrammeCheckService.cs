using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IProgrammeCheckService"/>
/// <remarks>
/// Программу читает тот же <see cref="IDisciplineDateSource"/>, что и деление, а план деления
/// берётся у <see cref="ICompetitionDaySplitService"/> (dry-run): карточка и кнопка видят ровно то,
/// что потом запишет деление (правило 1 data-integrity). Алгоритм деления здесь не повторяется.
/// </remarks>
public sealed class ProgrammeCheckService(
    SwimmDbContext db,
    IDisciplineDateSource dateSource,
    ICompetitionDaySplitService splitter,
    ILogger<ProgrammeCheckService> logger) : IProgrammeCheckService
{
    private const string DateFormat = "dd/MM/yyyy";

    public async Task<ProgrammeCheckVerdict> CheckAsync(int discoveryId, CancellationToken ct = default) =>
        (await CheckCoreAsync(discoveryId, ct)).Verdict;

    public async Task<ProgrammeSplitOutcome> SplitAsync(int discoveryId, CancellationToken ct = default)
    {
        var (verdict, logligId) = await CheckCoreAsync(discoveryId, ct);
        if (verdict.SplitCompetitionId is not int competitionId)
            return ProgrammeSplitOutcome.Refused(verdict.SplitBlocked ?? $"делить нечего: {verdict.Headline}");

        // Сервис деления сам заново строит план и откажет целиком, если что-то изменилось (всё или ничего).
        var report = await splitter.SplitAsync(competitionId, logligId, apply: true, ct);
        return report.Applied
            ? new ProgrammeSplitOutcome(null, report)
            : ProgrammeSplitOutcome.Refused(report.Refusal ?? "деление не применено");
    }

    private async Task<(ProgrammeCheckVerdict Verdict, int? LogligId)> CheckCoreAsync(int discoveryId, CancellationToken ct)
    {
        var row = await db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.Id == discoveryId)
            .Select(d => new { d.OrgCompId, d.LogligId, d.DateStart, d.DateEnd })
            .FirstOrDefaultAsync(ct);
        if (row == null)
            return (new ProgrammeCheckVerdict(ProgrammeVerdictKind.None, $"Строка входящих #{discoveryId} не найдена.", [], null, null, null, false), null);

        var days = await CompetitionIdentity.ResolveDaysAsync(db, row.OrgCompId, ct);
        var dbDays = days.Select(c => ParseDate(c.Date)).OfType<DateOnly>().Distinct().ToList();

        IReadOnlyList<DateOnly>? programme = null;
        string? error = null;
        if (row.LogligId is int loglig)
        {
            try
            {
                programme = (await dateSource.FetchAsync(loglig, ct)).Days;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Программа loglig {LogligId} для discovery #{DiscoveryId} не получена", loglig, discoveryId);
                error = ex.Message;
            }
        }

        CompetitionDaySplitReport? plan = null;
        if (error == null && days.Count == 1 && ProgrammeCheckRules.NeedsSplitPlan(dbDays, programme))
            plan = await splitter.SplitAsync(days[0].Id, row.LogligId, apply: false, ct);

        var verdict = ProgrammeCheckRules.Verdict(new ProgrammeCheckInput(
            DateOnly.FromDateTime(row.DateStart), DateOnly.FromDateTime(row.DateEnd), dbDays,
            row.LogligId, programme, error, plan));
        return (verdict, row.LogligId);
    }

    private static DateOnly? ParseDate(string? date) =>
        DateOnly.TryParseExact(date?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
