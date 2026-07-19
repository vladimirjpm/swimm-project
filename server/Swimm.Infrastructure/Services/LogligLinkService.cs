using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Оркестрация привязки Loglig ID к пловцу (docs/loglig-id-plan.md, шаг 5): автопоиск кандидатов
/// + сверка (ICandidateSearchProvider/ILogligClient/ILogligMatchService) либо ручная привязка
/// админом. Правила см. в задании (docs/tasks/loglig-admin-ui-sonnet.md, «Решения»).
/// </summary>
public class LogligLinkService(
    SwimmDbContext db,
    ICandidateSearchProvider searchProvider,
    ILogligClient logligClient,
    ILogligMatchService matchService,
    ILogger<LogligLinkService> logger) : ILogligLinkService
{
    public async Task<IReadOnlyList<LogligSwimmerRow>> ListAsync(string? query, string? status, int take, CancellationToken ct)
    {
        var q = db.Swimmers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            q = q.Where(s => s.LastName.Contains(needle) || s.FirstName.Contains(needle)
                || s.LastNameEn.Contains(needle) || s.FirstNameEn.Contains(needle));
        }

        q = status switch
        {
            "linked" => q.Where(s => s.LogligId != null),
            "unlinked" => q.Where(s => s.LogligId == null),
            _ => q
        };

        return await q
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Take(take)
            .Select(s => new LogligSwimmerRow(
                s.Id, s.LastName, s.FirstName, s.BirthYear,
                s.Club != null ? s.Club.Name : null,
                s.LogligId, s.LogligIdStatus, s.LogligIdSource, s.LogligIdVerifiedAt))
            .ToListAsync(ct);
    }

    public async Task<LogligLinkResult> FindAndLinkAsync(int swimmerId, CancellationToken ct)
    {
        var swimmer = await db.Swimmers.FindAsync([swimmerId], ct);
        if (swimmer is null)
            return new LogligLinkResult(false, "Пловец не найден", searchProvider.IsConfigured, []);
        if (swimmer.LogligIdStatus == "Verified")
            return new LogligLinkResult(false, "Пловец уже привязан — сначала отвяжите", searchProvider.IsConfigured, []);

        if (!searchProvider.IsConfigured)
            return new LogligLinkResult(false, null, false, []);

        var candidateIds = await searchProvider.FindCandidatesAsync(swimmer.LastName, swimmer.FirstName, ct);
        if (candidateIds.Count == 0)
            return new LogligLinkResult(false, null, true, []);

        var localResults = await LoadLocalResultsAsync(swimmerId, ct);
        var clubName = await GetClubNameAsync(swimmer.ClubId, ct);

        var candidates = new List<LogligCandidateInfo>();
        foreach (var logligId in candidateIds)
        {
            var card = await logligClient.GetPlayerCardAsync(logligId, ct);
            if (card is null) continue;

            var report = matchService.Match(card, swimmer.BirthYear, clubName, localResults);
            candidates.Add(new LogligCandidateInfo(
                logligId, card.FullName, card.BirthYear, card.ClubName,
                report.Decision, report.BirthYearMatch, report.ClubNameMatch, report.MatchedResultCount));
        }

        var autoVerify = candidates.Where(c => c.Decision == LogligMatchDecision.AutoVerify).ToList();
        if (autoVerify.Count != 1)
            return new LogligLinkResult(false, null, true, candidates);

        var chosen = autoVerify[0];
        var (linked, error) = await TryLinkAsync(swimmer, chosen.LogligId, "auto", ct);
        return new LogligLinkResult(linked, error, true, linked ? [] : candidates);
    }

    public async Task<LogligLinkResult> SetManualAsync(int swimmerId, int logligId, CancellationToken ct)
    {
        var swimmer = await db.Swimmers.FindAsync([swimmerId], ct);
        if (swimmer is null)
            return new LogligLinkResult(false, "Пловец не найден", searchProvider.IsConfigured, []);
        if (swimmer.LogligIdStatus == "Verified")
            return new LogligLinkResult(false, "Пловец уже привязан — сначала отвяжите", searchProvider.IsConfigured, []);

        var card = await logligClient.GetPlayerCardAsync(logligId, ct);
        if (card is null)
            return new LogligLinkResult(false, $"Карточка loglig #{logligId} недоступна", searchProvider.IsConfigured, []);

        // Сверку возвращаем как информацию — на решение админа она не влияет.
        var localResults = await LoadLocalResultsAsync(swimmerId, ct);
        var clubName = await GetClubNameAsync(swimmer.ClubId, ct);
        var report = matchService.Match(card, swimmer.BirthYear, clubName, localResults);
        var info = new LogligCandidateInfo(
            logligId, card.FullName, card.BirthYear, card.ClubName,
            report.Decision, report.BirthYearMatch, report.ClubNameMatch, report.MatchedResultCount);

        var (linked, error) = await TryLinkAsync(swimmer, logligId, "admin", ct);
        return new LogligLinkResult(linked, error, searchProvider.IsConfigured, linked ? [] : [info]);
    }

    public async Task<bool> UnlinkAsync(int swimmerId, CancellationToken ct)
    {
        var swimmer = await db.Swimmers.FindAsync([swimmerId], ct);
        if (swimmer is null || swimmer.LogligId is null) return false;

        swimmer.LogligId = null;
        swimmer.LogligIdStatus = null;
        swimmer.LogligIdSource = null;
        swimmer.LogligIdSuggestedByUserId = null;
        swimmer.LogligIdSuggestedAt = null;
        swimmer.LogligIdVerifiedAt = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Сохраняет привязку с проверкой занятости LogligId (AnyAsync + перехват гонки на SaveChanges).</summary>
    private async Task<(bool Linked, string? Error)> TryLinkAsync(
        Swimm.Domain.Entities.Swimmer swimmer, int logligId, string source, CancellationToken ct)
    {
        var holder = await db.Swimmers.AsNoTracking()
            .Where(s => s.LogligId == logligId && s.Id != swimmer.Id)
            .Select(s => new { s.Id, Name = s.LastName + " " + s.FirstName })
            .FirstOrDefaultAsync(ct);
        if (holder is not null)
            return (false, $"loglig ID {logligId} уже привязан к пловцу {holder.Name} #{holder.Id}");

        swimmer.LogligId = logligId;
        swimmer.LogligIdStatus = "Verified";
        swimmer.LogligIdSource = source;
        swimmer.LogligIdVerifiedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Гонка: кто-то занял этот logligId между проверкой и сохранением.
            var raceHolder = await db.Swimmers.AsNoTracking()
                .Where(s => s.LogligId == logligId && s.Id != swimmer.Id)
                .Select(s => new { s.Id, Name = s.LastName + " " + s.FirstName })
                .FirstOrDefaultAsync(ct);
            swimmer.LogligId = null;
            swimmer.LogligIdStatus = null;
            swimmer.LogligIdSource = null;
            swimmer.LogligIdVerifiedAt = null;
            db.Entry(swimmer).State = EntityState.Unchanged;
            return (false, raceHolder is not null
                ? $"loglig ID {logligId} уже привязан к пловцу {raceHolder.Name} #{raceHolder.Id}"
                : "Не удалось сохранить привязку (конфликт)");
        }

        logger.LogWarning(
            "Admin loglig link: swimmer #{SwimmerId} ← loglig #{LogligId}, source={Source}",
            swimmer.Id, logligId, source);
        return (true, null);
    }

    private async Task<string?> GetClubNameAsync(int? clubId, CancellationToken ct)
    {
        if (clubId is null) return null;
        return await db.Clubs.AsNoTracking().Where(c => c.Id == clubId).Select(c => c.Name).FirstOrDefaultAsync(ct);
    }

    /// <summary>Наши результаты пловца для сверки: без эстафет (RelayId != null), join Style.</summary>
    private async Task<IReadOnlyList<LocalResultKey>> LoadLocalResultsAsync(int swimmerId, CancellationToken ct)
        => await db.Results.AsNoTracking()
            .Where(r => r.SwimmerId == swimmerId && r.RelayId == null)
            .Select(r => new LocalResultKey(r.CompetitionDate, r.Distance, r.Style.Name, r.TimeMillisecond))
            .ToListAsync(ct);
}
