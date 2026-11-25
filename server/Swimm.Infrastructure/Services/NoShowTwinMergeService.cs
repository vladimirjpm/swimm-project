using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services.DataChecks;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// <see cref="INoShowTwinMergeService"/>: надёжные пары (<see cref="NoShowReasons.ReliableTwin"/>) —
/// сначала пробный прогон склейки, потом только пары без конфликтов. Аудит на каждую склейку.
/// Сбой не должен ронять импорт — вызывающий держит try/catch.
/// </summary>
public sealed class NoShowTwinMergeService(
    SwimmDbContext db, ISwimmerMergeService merge, IAdminAuditService audit, ISettingsService settings,
    ILogger<NoShowTwinMergeService> logger) : INoShowTwinMergeService
{
    public async Task<NoShowTwinMergeReport> MergeAsync(IReadOnlyCollection<int> competitionIds, CancellationToken ct = default)
    {
        if (competitionIds.Count == 0 || !settings.GetValue(NoShowTwinMergeRules.SettingKey, true))
            return NoShowTwinMergeReport.Empty;

        var cases = await NoShowCases.LoadAsync(db, competitionIds, ct);
        var found = cases
            .Select(c => (Case: c, Pair: NoShowReasons.ReliableTwin(c.Entry, c.Lane, c.Own)))
            .Where(x => x.Pair is not null)
            .Select(x => (x.Case, Pair: x.Pair!.Value))
            .ToList();
        if (found.Count == 0) return NoShowTwinMergeReport.Empty;

        // Одна пара — одна склейка, и ни одна карточка не бывает в двух парах разными ролями:
        // «A ← B» и «B ← C» в одном прогоне — повод посмотреть глазами, а не цепочка склеек.
        var pairs = found.Select(x => x.Pair).Distinct().ToList();
        var roles = pairs.SelectMany(p => new[] { p.Canonical, p.Duplicate }).GroupBy(id => id)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var skipped = pairs.Where(p => roles.Contains(p.Canonical) || roles.Contains(p.Duplicate))
            .Select(p => $"#{p.Canonical} ← #{p.Duplicate}: карточка в нескольких парах — смотреть глазами").ToList();
        var candidates = pairs.Where(p => !roles.Contains(p.Canonical) && !roles.Contains(p.Duplicate))
            .Select(p => new SwimmerMergePair(p.Canonical, p.Duplicate)).ToList();
        if (candidates.Count == 0) return new([], skipped);

        var dry = await merge.MergeAsync(candidates, dryRun: true, ct);
        var clean = dry.Pairs.Where(p => p.Status == "dry-run" && p.Conflicts.Count == 0)
            .Select(p => new SwimmerMergePair(p.CanonicalId, p.DuplicateId)).ToList();
        skipped.AddRange(dry.Pairs.Where(p => p.Status != "dry-run" || p.Conflicts.Count > 0)
            .Select(p => $"#{p.CanonicalId} ← #{p.DuplicateId}: {string.Join("; ", p.Conflicts.DefaultIfEmpty(p.Status))}"));
        if (clean.Count == 0) return new([], skipped);

        var applied = await merge.MergeAsync(clean, dryRun: false, ct);
        var merged = new List<string>();
        foreach (var p in applied.Pairs.Where(p => p.Status == "merged"))
        {
            var x = found.First(f => f.Pair == (p.CanonicalId, p.DuplicateId));
            var dupName = x.Case.Entry.Swimmer.Id == p.DuplicateId
                ? x.Case.Entry.Swimmer.Name
                : x.Case.Lane.First(r => r.Swimmer.Id == p.DuplicateId).Swimmer.Name;
            var line = $"#{p.CanonicalId} ← #{p.DuplicateId} {dupName}";
            merged.Add(line);
            await audit.LogAsync(NoShowTwinMergeRules.AuditAction, "Swimmer", p.CanonicalId.ToString(),
                $"Склеен двойник по неявке стартового протокола: {line} — {x.Case.Reason.Text}",
                new
                {
                    p.CanonicalId,
                    p.DuplicateId,
                    x.Case.CompetitionId,
                    Discipline = $"{x.Case.Distance} {x.Case.Style}",
                    x.Case.Entry.Heat,
                    x.Case.Entry.Lane,
                    p.Actions,
                }, CancellationToken.None);
        }
        skipped.AddRange(applied.Pairs.Where(p => p.Status != "merged")
            .Select(p => $"#{p.CanonicalId} ← #{p.DuplicateId}: {p.Status} {string.Join("; ", p.Conflicts)}"));

        if (merged.Count > 0)
            logger.LogInformation("Автосклейка двойников по неявкам: {Merged}", string.Join(", ", merged));
        return new(merged, skipped);
    }
}
