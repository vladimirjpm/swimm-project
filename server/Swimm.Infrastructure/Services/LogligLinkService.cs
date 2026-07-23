using System.Text.RegularExpressions;
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
            "suggested" => q.Where(s => s.LogligIdStatus == "Suggested"),
            "rejected" => q.Where(s => s.LogligIdStatus == "Rejected"),
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

    public async Task<LogligBatchReport> RunBatchAsync(int take, CancellationToken ct)
    {
        if (take <= 0 || !searchProvider.IsConfigured)
            return new LogligBatchReport(0, 0, 0, 0);

        // Кандидаты на прогон: без какого-либо loglig-статуса (Suggested/Rejected не трогаем —
        // ими занимается краудсорс-цикл), с ивритским именем (loglig ищется по ивриту;
        // латиница/хэши только жгли бы квоту) и хотя бы одним личным результатом; сначала — с
        // наибольшим числом результатов (там сверке есть за что зацепиться).
        // Regex.IsMatch Npgsql транслирует в SQL-оператор ~, InMemory-провайдер тестов
        // исполняет в памяти.
        var targets = await db.Swimmers.AsNoTracking()
            .Where(s => s.LogligIdStatus == null && s.LogligId == null)
            .Where(s => Regex.IsMatch(s.LastName, "[א-ת]") || Regex.IsMatch(s.FirstName, "[א-ת]"))
            .Select(s => new
            {
                s.Id,
                ResultCount = db.Results.Count(r => r.SwimmerId == s.Id && r.RelayId == null),
            })
            .Where(x => x.ResultCount > 0)
            .OrderByDescending(x => x.ResultCount)
            .Take(take)
            .ToListAsync(ct);

        int linked = 0, withCandidates = 0, nothingFound = 0;
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            var result = await FindAndLinkAsync(target.Id, ct);
            if (result.Linked) linked++;
            else if (result.Candidates.Count > 0) withCandidates++;
            else nothingFound++;
        }

        logger.LogInformation(
            "Loglig batch: обработано {Processed}, привязано {Linked}, с кандидатами {WithCandidates}, впустую {NothingFound}",
            targets.Count, linked, withCandidates, nothingFound);
        return new LogligBatchReport(targets.Count, linked, withCandidates, nothingFound);
    }


    /// <summary>Сохраняет привязку с проверкой занятости LogligId (AnyAsync + перехват гонки на SaveChanges).</summary>
    private async Task<(bool Linked, string? Error)> TryLinkAsync(
        Swimm.Domain.Entities.Swimmer swimmer, int logligId, string source, CancellationToken ct)
    {
        var holder = await db.Swimmers
            .Where(s => s.LogligId == logligId && s.Id != swimmer.Id)
            .FirstOrDefaultAsync(ct);
        if (holder is not null)
        {
            // Rejected-держатель — ошибочное отклонённое предложение (шаг 6), уникальный индекс
            // один на колонку: освобождаем слот, легитимная привязка важнее анти-спам-метки.
            if (holder.LogligIdStatus != "Rejected")
                return (false, $"loglig ID {logligId} уже привязан к пловцу {holder.LastName} {holder.FirstName} #{holder.Id}");

            holder.LogligId = null;
            holder.LogligIdStatus = null;
            holder.LogligIdSource = null;
            holder.LogligIdSuggestedByUserId = null;
            holder.LogligIdSuggestedAt = null;
            holder.LogligIdVerifiedAt = null;
        }

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
