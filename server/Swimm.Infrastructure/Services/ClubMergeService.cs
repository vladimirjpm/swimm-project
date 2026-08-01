using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Склейка клубов-дублей (docs/tasks/club-merge-plan.md, фаза B). Перевешивает на
/// канонический: Results, Swimmers, HubGroups, Sys_HubGroupClubRequests, Sys_UserFavorites;
/// пустые NameEn/CountryId канонического дозаполняет из дубля; дубль помечает
/// <see cref="Club.MergedIntoId"/> (мягкое слияние — строка остаётся, чтобы ссылки на
/// старый Id не гнили; из публичных выборок и дедупа склеенные исключаются).
/// Guard 1: у ОБОИХ клубов официальная hub-группа (partial unique index
/// HubGroups.ClubId WHERE IsOfficial) — пара блокируется. Guard 2: дубль-строки
/// Sys_UserFavorites (клуб дважды в избранном одного юзера) схлопываются в одну.
/// Все изменения — одним SaveChanges; dry-run не пишет ничего. Tracked-сущности
/// (InMemory-совместимо): одноразовая админ-операция, худший случай — тысячи строк.
/// После реального merge кэш сбрасывается целиком (club-summary и публичные выдачи
/// денормализуют клуб).
/// </summary>
public class ClubMergeService(SwimmDbContext db, ICacheService cache, IClubStandingService standings)
    : IClubMergeService
{
    public async Task<ClubMergeReport> MergeAsync(
        IReadOnlyList<ClubMergePair> pairs, bool dryRun = true, CancellationToken ct = default)
    {
        // Валидация набора пар до обращений к БД. В отличие от пловцов, повтор
        // КАНОНА — норма: один чистый клуб принимает сразу несколько мусорных
        // вариантов («בני הרצליה» + 5 хвостов). Ошибки ввода: повторяющийся дубль
        // и цепочка (один Id и канон, и дубль — порядок применения стал бы значимым).
        var canonicalIds = new HashSet<int>();
        var duplicateIds = new HashSet<int>();
        var repeatedDups = new List<int>();
        foreach (var pair in pairs)
        {
            if (pair.CanonicalId == pair.DuplicateId) continue;
            canonicalIds.Add(pair.CanonicalId);
            if (!duplicateIds.Add(pair.DuplicateId)) repeatedDups.Add(pair.DuplicateId);
        }
        if (repeatedDups.Count > 0)
        {
            throw new ArgumentException(
                $"Один и тот же дубль в нескольких парах (Id: {string.Join(", ", repeatedDups.Distinct())}).");
        }
        var chained = canonicalIds.Intersect(duplicateIds).ToList();
        if (chained.Count > 0)
        {
            throw new ArgumentException(
                $"Цепочка склеек: клуб одновременно канон и дубль в разных парах (Id: {string.Join(", ", chained)}). Разбейте на отдельные вызовы.");
        }

        var report = new ClubMergeReport { DryRun = dryRun };
        var anyMerged = false;

        // Избранное, уже перевешенное предыдущими парами ЭТОГО вызова (до SaveChanges
        // запрос к БД их не видит) — учитываем при дедупе, иначе общий канон
        // получил бы две строки одного юзера.
        var movedFavs = new HashSet<(int UserId, int ClubId)>();

        // Каноны, получившие официальную группу от дубля в ЭТОМ вызове (см. movedFavs).
        var gotOfficial = new HashSet<int>();

        foreach (var pair in pairs)
        {
            var res = new ClubMergePairResult { CanonicalId = pair.CanonicalId, DuplicateId = pair.DuplicateId };
            report.Pairs.Add(res);

            if (pair.CanonicalId == pair.DuplicateId)
            {
                res.Status = "error";
                res.Conflicts.Add("canonical и duplicate совпадают");
                continue;
            }

            var canonical = await db.Clubs.FirstOrDefaultAsync(c => c.Id == pair.CanonicalId, ct);
            var duplicate = await db.Clubs.FirstOrDefaultAsync(c => c.Id == pair.DuplicateId, ct);
            if (canonical is null || duplicate is null)
            {
                res.Status = "error";
                res.Conflicts.Add($"клуб не найден: {(canonical is null ? pair.CanonicalId : pair.DuplicateId)}");
                continue;
            }
            if (IsSynthetic(canonical) || IsSynthetic(duplicate))
            {
                res.Status = "error";
                res.Conflicts.Add("синтетические клубы (SYNTH%) не мержатся");
                continue;
            }
            // Guard 0: уже склеенный клуб не может участвовать снова. Как дубль — потому что
            // его связи давно переехали, как канон — потому что получилась бы цепочка
            // A → B → C, и /clubs/{A} пришлось бы разматывать рекурсивно.
            if (duplicate.MergedIntoId is not null || canonical.MergedIntoId is not null)
            {
                res.Status = "error";
                var merged = duplicate.MergedIntoId is not null ? duplicate.Id : canonical.Id;
                res.Conflicts.Add($"клуб {merged} уже склеен в другой — повторный merge не имеет смысла");
                continue;
            }

            // Guard 1: официальная группа уникальна по клубу (partial unique index) —
            // если официальные группы есть у обоих, перецеливание нарушит индекс.
            var canonOfficial = gotOfficial.Contains(canonical.Id)
                || await db.HubGroups.AsNoTracking()
                    .AnyAsync(g => g.ClubId == canonical.Id && g.IsOfficial, ct);
            var dupOfficial = await db.HubGroups.AsNoTracking()
                .AnyAsync(g => g.ClubId == duplicate.Id && g.IsOfficial, ct);
            if (canonOfficial && dupOfficial)
            {
                res.Status = "conflict";
                res.Conflicts.Add(
                    "у обоих клубов есть официальная hub-группа — сначала снимите официальный статус с одной из них");
                continue;
            }

            // --- Перенос связей ---

            var results = await db.Results.Where(r => r.ClubId == duplicate.Id).ToListAsync(ct);
            foreach (var r in results) r.ClubId = canonical.Id;
            Note(res, "Results", results.Count);

            var swimmers = await db.Swimmers.Where(s => s.ClubId == duplicate.Id).ToListAsync(ct);
            foreach (var s in swimmers) s.ClubId = canonical.Id;
            Note(res, "Swimmers", swimmers.Count);

            var groups = await db.HubGroups.Where(g => g.ClubId == duplicate.Id).ToListAsync(ct);
            foreach (var g in groups) g.ClubId = canonical.Id;
            Note(res, "HubGroups", groups.Count);
            if (dupOfficial) gotOfficial.Add(canonical.Id);

            var requests = await db.HubGroupClubRequests.Where(r => r.ClubId == duplicate.Id).ToListAsync(ct);
            foreach (var r in requests) r.ClubId = canonical.Id;
            Note(res, "Sys_HubGroupClubRequests", requests.Count);

            // Guard 2: клуб может оказаться в избранном юзера дважды — строку дубля удаляем.
            var favs = await db.UserFavorites.Where(f => f.ClubId == duplicate.Id).ToListAsync(ct);
            var favUsersOfCanonical = await db.UserFavorites
                .Where(f => f.ClubId == canonical.Id).Select(f => f.UserId).ToListAsync(ct);
            foreach (var f in favs)
            {
                if (favUsersOfCanonical.Contains(f.UserId) || movedFavs.Contains((f.UserId, canonical.Id)))
                {
                    db.UserFavorites.Remove(f);
                    res.Actions.Add($"Sys_UserFavorites: строка user {f.UserId} удалена (canonical уже в избранном)");
                }
                else
                {
                    f.ClubId = canonical.Id;
                    movedFavs.Add((f.UserId, canonical.Id));
                }
            }
            Note(res, "Sys_UserFavorites", favs.Count);

            // Дозаполнение пустых полей канонического из дубля. Кросс-скриптовая пара:
            // канон — ивритская запись, латинское название дубля уходит в NameEn.
            if (string.IsNullOrEmpty(canonical.NameEn))
            {
                if (!string.IsNullOrEmpty(duplicate.NameEn))
                { canonical.NameEn = duplicate.NameEn; res.Actions.Add("Club.NameEn ← дубль (NameEn)"); }
                else if (!HasHebrew(duplicate.Name) && HasHebrew(canonical.Name))
                { canonical.NameEn = duplicate.Name; res.Actions.Add("Club.NameEn ← дубль (латинское Name)"); }
            }
            if (canonical.CountryId is null && duplicate.CountryId is not null)
            { canonical.CountryId = duplicate.CountryId; res.Actions.Add("Club.CountryId ← дубль"); }

            // Мягкое слияние: строку НЕ удаляем, а помечаем ссылкой на приёмника.
            // Иначе внешние ссылки и /clubs/{старый id} гниют после каждой чистки дублей.
            // Все связи выше уже переехали, так что склеенный клуб пуст.
            duplicate.MergedIntoId = canonical.Id;
            res.Actions.Add($"Club {duplicate.Id} склеен в {canonical.Id} (MergedIntoId)");
            res.Status = dryRun ? "dry-run" : "merged";
            anyMerged = true;
        }

        if (dryRun)
        {
            // План собран tracked-изменениями — откатываем трекер, БД не трогаем.
            db.ChangeTracker.Clear();
            return report;
        }

        await db.SaveChangesAsync(ct); // один SaveChanges = одна транзакция

        // Материализованный клубный зачёт помнит места ИСЧЕЗНУВШЕГО клуба — пересчитываем
        // соревнования канона (результаты дубля уже переехали на него, значит его список
        // соревнований покрывает и унаследованные).
        if (anyMerged)
        {
            foreach (var id in report.Pairs.Where(p => p.Status == "merged").Select(p => p.CanonicalId).Distinct())
                await standings.RebuildForClubAsync(id, ct);
        }

        // club-summary (фаза 3.4) и прочие публичные выдачи денормализуют клуб.
        if (anyMerged) await cache.InvalidateAllAsync();

        return report;
    }

    private static bool IsSynthetic(Club c) => c.Name.StartsWith("SYNTH", StringComparison.Ordinal);

    private static bool HasHebrew(string s) => s.Any(c => c is >= 'א' and <= 'ת');

    private static void Note(ClubMergePairResult res, string table, int count)
    {
        if (count > 0) res.Actions.Add($"{table}: {count} → canonical");
    }
}
