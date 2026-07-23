using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Склейка пловцов-дублей. Перевешивает на канонического: Results, Sys_UserFavorites,
/// HubGroupMembers, Sys_HubGroupUserMembers, Sys_UserMedia, Sys_HubGroupMedia,
/// Sys_TrainingResults, Sys_AppUsers.SwimmerId; пустые поля канонического дозаполняет
/// из дубля; дубль удаляет. Конфликты (общий заплыв, membership/favorite уже есть у
/// канонического — последние решаются удалением строки дубля) — см. по месту.
/// Все изменения — одним SaveChanges (одна транзакция); dry-run не пишет ничего.
/// </summary>
public class SwimmerMergeService(SwimmDbContext db) : ISwimmerMergeService
{
    public async Task<SwimmerMergeReport> MergeAsync(
        IReadOnlyList<SwimmerMergePair> pairs, bool dryRun = true, CancellationToken ct = default)
    {
        // A2: пересекающиеся пары (один и тот же Id в нескольких парах, в любой роли) —
        // ошибка ввода, отклоняем весь вызов до любых обращений к БД. Self-пары (canonical
        // == duplicate) в этот подсчёт не включаем — для них остаётся отдельный per-pair error.
        var idCounts = new Dictionary<int, int>();
        foreach (var pair in pairs)
        {
            if (pair.CanonicalId == pair.DuplicateId) continue;
            idCounts[pair.CanonicalId] = idCounts.GetValueOrDefault(pair.CanonicalId) + 1;
            idCounts[pair.DuplicateId] = idCounts.GetValueOrDefault(pair.DuplicateId) + 1;
        }
        var overlapping = idCounts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToList();
        if (overlapping.Count > 0)
        {
            throw new ArgumentException(
                $"Пересекающиеся пары: один и тот же пловец участвует в нескольких парах (Id: {string.Join(", ", overlapping)}). Разбейте на отдельные вызовы.");
        }

        var report = new SwimmerMergeReport { DryRun = dryRun };

        foreach (var pair in pairs)
        {
            var res = new SwimmerMergePairResult { CanonicalId = pair.CanonicalId, DuplicateId = pair.DuplicateId };
            report.Pairs.Add(res);

            if (pair.CanonicalId == pair.DuplicateId)
            {
                res.Status = "error";
                res.Conflicts.Add("canonical и duplicate совпадают");
                continue;
            }

            var canonical = await db.Swimmers.FirstOrDefaultAsync(s => s.Id == pair.CanonicalId, ct);
            var duplicate = await db.Swimmers.FirstOrDefaultAsync(s => s.Id == pair.DuplicateId, ct);
            if (canonical is null || duplicate is null)
            {
                res.Status = "error";
                res.Conflicts.Add($"пловец не найден: {(canonical is null ? pair.CanonicalId : pair.DuplicateId)}");
                continue;
            }
            if (IsSynthetic(canonical) || IsSynthetic(duplicate))
            {
                res.Status = "error";
                res.Conflicts.Add("синтетические пловцы (SYNTH-) не мержатся");
                continue;
            }

            // Общий заплыв: у обоих есть индивидуальный результат одного (соревнование,
            // стиль, дистанция) — это разные люди либо грязные данные; не мержим.
            var canonKeys = await db.Results.AsNoTracking()
                .Where(r => r.SwimmerId == canonical.Id && r.RelayId == null)
                .Select(r => new { r.CompetitionId, r.StyleId, r.Distance })
                .ToListAsync(ct);
            var dupKeys = await db.Results.AsNoTracking()
                .Where(r => r.SwimmerId == duplicate.Id && r.RelayId == null)
                .Select(r => new { r.CompetitionId, r.StyleId, r.Distance })
                .ToListAsync(ct);
            var shared = canonKeys.Intersect(dupKeys).ToList();
            if (shared.Count > 0)
            {
                res.Status = "conflict";
                res.Conflicts.AddRange(shared.Select(k =>
                    $"общий заплыв: competition {k.CompetitionId}, style {k.StyleId}, {k.Distance}"));
                continue;
            }

            // --- Перенос связей (tracked-сущности: InMemory-совместимо, объёмы небольшие) ---

            var results = await db.Results.Where(r => r.SwimmerId == duplicate.Id).ToListAsync(ct);
            foreach (var r in results) r.SwimmerId = canonical.Id;
            Note(res, "Results", results.Count);

            // Ноги эстафет: FK RelayMember→Swimmer = Restrict, уникальный (RelayId, SwimmerId).
            // Без переноса удаление дубля падает DbUpdateException. Если канонический уже нога
            // той же эстафеты (обычно дефект данных — один человек дважды) — строку дубля удаляем.
            var relayLegs = await db.RelayMembers.Where(m => m.SwimmerId == duplicate.Id).ToListAsync(ct);
            var canonRelays = await db.RelayMembers
                .Where(m => m.SwimmerId == canonical.Id).Select(m => m.RelayId).ToListAsync(ct);
            foreach (var leg in relayLegs)
            {
                if (canonRelays.Contains(leg.RelayId))
                {
                    db.RelayMembers.Remove(leg);
                    res.Actions.Add($"RelayMembers: нога эстафеты {leg.RelayId} удалена (canonical уже в составе)");
                }
                else leg.SwimmerId = canonical.Id;
            }
            Note(res, "RelayMembers", relayLegs.Count);

            // Favorites: у пользователя может быть избранным и канонический — тогда строку дубля удаляем.
            var favs = await db.UserFavorites.Where(f => f.SwimmerId == duplicate.Id).ToListAsync(ct);
            var favUsersOfCanonical = await db.UserFavorites
                .Where(f => f.SwimmerId == canonical.Id).Select(f => f.UserId).ToListAsync(ct);
            foreach (var f in favs)
            {
                if (favUsersOfCanonical.Contains(f.UserId))
                {
                    db.UserFavorites.Remove(f);
                    res.Actions.Add($"Sys_UserFavorites: строка user {f.UserId} удалена (canonical уже в избранном)");
                }
                else f.SwimmerId = canonical.Id;
            }
            Note(res, "Sys_UserFavorites", favs.Count);

            // Членства в группах: уникальный (HubGroupId, SwimmerId) — дубль-строку удаляем.
            var members = await db.HubGroupMembers.Where(m => m.SwimmerId == duplicate.Id).ToListAsync(ct);
            var canonGroups = await db.HubGroupMembers
                .Where(m => m.SwimmerId == canonical.Id).Select(m => m.HubGroupId).ToListAsync(ct);
            foreach (var m in members)
            {
                if (canonGroups.Contains(m.HubGroupId))
                {
                    db.HubGroupMembers.Remove(m);
                    res.Actions.Add($"HubGroupMembers: строка группы {m.HubGroupId} удалена (canonical уже в группе)");
                }
                else m.SwimmerId = canonical.Id;
            }
            Note(res, "HubGroupMembers", members.Count);

            var userMembers = await db.HubGroupUserMembers.Where(m => m.SwimmerId == duplicate.Id).ToListAsync(ct);
            foreach (var m in userMembers) m.SwimmerId = canonical.Id;
            Note(res, "Sys_HubGroupUserMembers", userMembers.Count);

            var userMedia = await db.UserMedia.Where(m => m.SwimmerId == duplicate.Id).ToListAsync(ct);
            foreach (var m in userMedia) m.SwimmerId = canonical.Id;
            Note(res, "Sys_UserMedia", userMedia.Count);

            var groupMedia = await db.HubGroupMedia.Where(m => m.SwimmerId == duplicate.Id).ToListAsync(ct);
            foreach (var m in groupMedia) m.SwimmerId = canonical.Id;
            Note(res, "Sys_HubGroupMedia", groupMedia.Count);

            var trainings = await db.TrainingResults.Where(t => t.SwimmerId == duplicate.Id).ToListAsync(ct);
            foreach (var t in trainings) t.SwimmerId = canonical.Id;
            Note(res, "Sys_TrainingResults", trainings.Count);

            var appUsers = await db.AppUsers.Where(u => u.SwimmerId == duplicate.Id).ToListAsync(ct);
            foreach (var u in appUsers) u.SwimmerId = canonical.Id;
            Note(res, "Sys_AppUsers.SwimmerId", appUsers.Count);

            // Дозаполнение пустых полей канонического из дубля (данные не теряем).
            if (string.IsNullOrEmpty(canonical.Gender) && !string.IsNullOrEmpty(duplicate.Gender))
            { canonical.Gender = duplicate.Gender; res.Actions.Add("Swimmer.Gender ← дубль"); }
            if (canonical.ClubId is null && duplicate.ClubId is not null)
            { canonical.ClubId = duplicate.ClubId; res.Actions.Add("Swimmer.ClubId ← дубль"); }
            if (canonical.CountryId is null && duplicate.CountryId is not null)
            { canonical.CountryId = duplicate.CountryId; res.Actions.Add("Swimmer.CountryId ← дубль"); }
            if (string.IsNullOrEmpty(canonical.LastNameEn) && !string.IsNullOrEmpty(duplicate.LastNameEn))
            {
                canonical.LastNameEn = duplicate.LastNameEn;
                canonical.FirstNameEn = duplicate.FirstNameEn;
                res.Actions.Add("Swimmer.*NameEn ← дубль");
            }
            if (string.IsNullOrEmpty(canonical.SwimmerOrgId) && !string.IsNullOrEmpty(duplicate.SwimmerOrgId))
            { canonical.SwimmerOrgId = duplicate.SwimmerOrgId; res.Actions.Add("Swimmer.SwimmerOrgId ← дубль"); }
            if (string.IsNullOrEmpty(canonical.AvatarUrl) && !string.IsNullOrEmpty(duplicate.AvatarUrl))
            { canonical.AvatarUrl = duplicate.AvatarUrl; res.Actions.Add("Swimmer.AvatarUrl ← дубль"); }

            db.Swimmers.Remove(duplicate);
            res.Actions.Add($"Swimmer {duplicate.Id} удалён");
            res.Status = dryRun ? "dry-run" : "merged";
        }

        if (dryRun)
        {
            // План собран tracked-изменениями — откатываем трекер, БД не трогаем.
            db.ChangeTracker.Clear();
            return report;
        }

        await db.SaveChangesAsync(ct); // один SaveChanges = одна транзакция
        return report;
    }

    private static bool IsSynthetic(Swimm.Domain.Entities.Swimmer s) =>
        s.SwimmerOrgId is not null && s.SwimmerOrgId.StartsWith("SYNTH-", StringComparison.Ordinal);

    private static void Note(SwimmerMergePairResult res, string table, int count)
    {
        if (count > 0) res.Actions.Add($"{table}: {count} → canonical");
    }
}
