using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Р125: пол пловца — с карточки loglig (см. <see cref="ILogligCardGenderService"/>). Вердикт —
/// чистое <see cref="LogligCardGenderRule"/>; здесь выборка, сеть, суточный ответ и запись.
///
/// • Сеть — только по int loglig-id, последовательно, не чаще раза в <see cref="DefaultRequestGap"/>,
///   не больше потолка запросов на прогон; вне транзакции импорта (шаг идёт после него, в своём scope).
/// • Свежий (моложе суток, тот же loglig-id, записан с именем) ответ из <c>Sys_SwimmerLogligClubs</c>
///   берётся без сети; каждый удачный визит туда же и пишется — вместе с клубом (Р112).
/// • Запись пола — <see cref="IDataCheckRunner.SetSwimmerGenderAsync"/>: то же действие, что у кнопок
///   ♂/♀ в Health. Перед записью пол перечитывается: проставленный за это время не трогаем.
/// </summary>
public sealed class LogligCardGenderService(
    SwimmDbContext db,
    ILogligClient loglig,
    IDataCheckRunner checks,
    IAdminAuditService audit,
    ILogger<LogligCardGenderService> logger,
    TimeSpan? requestGap = null) : ILogligCardGenderService
{
    /// <summary>Пауза между запросами к loglig — как у сверки клуба (Р98).</summary>
    public static readonly TimeSpan DefaultRequestGap = SwimmerClubLogligCheckService.DefaultRequestGap;

    private readonly TimeSpan _gap = requestGap ?? DefaultRequestGap;
    private DateTime? _lastRequestAt;

    public async Task<LogligCardGenderReport> FillFromFindingsAsync(CancellationToken ct = default)
    {
        var ids = await db.DataCheckFindings.AsNoTracking()
            .Where(f => f.Resolution == null && f.FixKind == DataCheckFixKinds.SwimmerGender && f.FixEntityId != null)
            .Select(f => f.FixEntityId!.Value)
            .Distinct()
            .ToListAsync(ct);
        return await RunAsync(db.Swimmers.Where(s => ids.Contains(s.Id)), "button",
            LogligCardGenderRule.MaxRequestsPerButton, ct);
    }

    public Task<LogligCardGenderReport> FillAfterImportAsync(int orgCompId, CancellationToken ct = default) =>
        RunAsync(db.Results.Where(r => r.Competition!.OrgCompId == orgCompId).Select(r => r.Swimmer).Distinct(),
            "import", LogligCardGenderRule.MaxRequestsPerImport, ct, new { OrgCompId = orgCompId });

    public Task<LogligCardGenderReport> FillDayAfterImportAsync(int competitionId, CancellationToken ct = default) =>
        RunAsync(db.Results.Where(r => r.CompetitionId == competitionId).Select(r => r.Swimmer).Distinct(),
            "import", LogligCardGenderRule.MaxRequestsPerImport, ct, new { CompetitionId = competitionId });

    private sealed record Row(int Id, string FirstName, string LastName, int BirthYear, string? Gender,
        int? LogligId, string? LogligIdStatus);

    private async Task<LogligCardGenderReport> RunAsync(
        IQueryable<Swimmer> scope, string trigger, int maxRequests, CancellationToken ct, object? extra = null)
    {
        var all = await scope.AsNoTracking()
            .Select(s => new Row(s.Id, s.FirstName, s.LastName, s.BirthYear, s.Gender, s.LogligId, s.LogligIdStatus))
            .ToListAsync(ct);
        var alreadyKnown = all.Count(s => LogligCardGenderRule.GenderKnown(s.Gender));
        var swimmers = all.Where(s => !LogligCardGenderRule.GenderKnown(s.Gender)).OrderBy(s => s.Id).ToList();

        var shared = await SwimmerLogligClubStore.LoadAsync(db, swimmers.Select(s => s.Id).ToArray(), ct);

        int set = 0, rows = 0, noGender = 0, failed = 0, mismatch = 0, notVerified = 0, overLimit = 0,
            requests = 0, fromCache = 0;
        var notes = new List<string>();

        foreach (var s in swimmers)
        {
            ct.ThrowIfCancellationRequested();
            var subject = new CardGenderSwimmer(s.Gender, s.LogligId, s.LogligIdStatus,
                LogligClient.NameTokens($"{s.FirstName} {s.LastName}"), s.BirthYear);

            // (б) без подтверждённого id на loglig не ходим вовсе.
            if (LogligCardGenderRule.Decide(subject, null).Verdict == CardGenderVerdict.NotVerified)
            {
                notVerified++;
                continue;
            }

            var lid = s.LogligId!.Value;
            StoredLogligClub? answer = shared.GetValueOrDefault(s.Id);
            var cached = LogligCardGenderRule.CacheValid(DateTime.UtcNow, answer, lid);
            if (cached) fromCache++;
            else
            {
                if (requests >= maxRequests)
                {
                    overLimit++;
                    continue;
                }

                await ThrottleAsync(ct);
                var card = await loglig.GetPlayerCardAsync(lid, ct: ct);
                requests++;
                if (card is null)
                {
                    failed++;
                    notes.Add($"#{s.Id} {s.LastName} {s.FirstName}: карточка loglig #{lid} не открылась");
                    continue;
                }

                var at = DateTime.UtcNow;
                var club = card.ClubName?.Trim();
                await SwimmerLogligClubStore.UpsertAsync(db, s.Id, lid, club, at, ct, card);
                await db.SaveChangesAsync(ct);
                answer = new StoredLogligClub(lid, club, at, card.FullName, card.BirthYear,
                    LogligCardGenderRule.ToSwimmerGender(card.Gender));
            }

            var (verdict, gender) = LogligCardGenderRule.Decide(subject,
                new CardGenderAnswer(LogligClient.NameTokens(answer!.CardName ?? ""), answer.CardBirthYear, answer.Gender));

            switch (verdict)
            {
                case CardGenderVerdict.CardHasNoGender:
                    noGender++;
                    notes.Add($"#{s.Id} {s.LastName} {s.FirstName}: на карточке loglig #{lid} пол не указан");
                    break;

                case CardGenderVerdict.NameMismatch or CardGenderVerdict.YearMismatch:
                    mismatch++;
                    notes.Add($"#{s.Id} {s.LastName} {s.FirstName} ({s.BirthYear}): карточка loglig #{lid} — "
                              + $"«{answer.CardName}» ({answer.CardBirthYear?.ToString() ?? "год не указан"}), "
                              + (verdict == CardGenderVerdict.YearMismatch ? "год не совпал" : "имя не совпало")
                              + " — пол не взят");
                    break;

                case CardGenderVerdict.Write:
                    // Пока шли по списку, пол мог проставить человек — его решение старше.
                    var current = await db.Swimmers.AsNoTracking().Where(x => x.Id == s.Id)
                        .Select(x => x.Gender).FirstOrDefaultAsync(ct);
                    if (LogligCardGenderRule.GenderKnown(current)) break;

                    if (await checks.SetSwimmerGenderAsync(s.Id, gender!, ct) is not int written) break;
                    set++;
                    rows += written;
                    await audit.LogAsync(LogligCardGenderRule.AuditAction, "Swimmer", s.Id.ToString(),
                        $"Пол '{gender}' пловцу #{s.Id} «{s.LastName} {s.FirstName}» — с карточки loglig #{lid} "
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
                            FromCache = cached,
                            Extra = extra,
                        }, CancellationToken.None);
                    break;
            }
        }

        var message = LogligCardGenderRule.Summary(swimmers.Count, set, rows, noGender, failed, mismatch,
            notVerified, overLimit, requests, fromCache);
        if (requests > 0 || set > 0)
            logger.LogInformation("Пол с карточек loglig ({Trigger}): {Message}", trigger, message);
        return new LogligCardGenderReport(swimmers.Count, set, rows, noGender, failed, mismatch, notVerified,
            overLimit, requests, fromCache, alreadyKnown, message, notes);
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
