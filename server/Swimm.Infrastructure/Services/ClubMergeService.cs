using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Склейка клубов-дублей (docs/tasks/club-merge-plan.md, фаза B). Перевешивает на
/// канонический: Results, Swimmers, HubGroups, Sys_HubGroupClubRequests, Sys_UserFavorites,
/// Sys_SwimmerClubCandidates (кандидат, у которого «было» = «стало» после склейки, снимается);
/// пустые NameEn/CountryId канонического дозаполняет из дубля; дубль помечает
/// <see cref="Club.MergedIntoId"/> (мягкое слияние — строка остаётся, чтобы ссылки на
/// старый Id не гнили; из публичных выборок и дедупа склеенные исключаются).
/// Guard 1: у ОБОИХ клубов официальная hub-группа (partial unique index
/// HubGroups.ClubId WHERE IsOfficial) — пара блокируется. Guard 2: дубль-строки
/// Sys_UserFavorites (клуб дважды в избранном одного юзера) схлопываются в одну.
/// Вид склейки (Н2, docs/plans/swimmer-club-change-plan.md §5): «Дубль» или «Переименование» —
/// у второго старое название уходит в ClubNames, а перенесённые строки помнят его (ClubNameId).
/// Цепочек не бывает (Н4): склеенные в дубль клубы перевешиваются на канон вместе с ним.
/// Все изменения — одним SaveChanges; dry-run не пишет ничего. Tracked-сущности
/// (InMemory-совместимо): одноразовая админ-операция, худший случай — тысячи строк.
/// После реального merge кэш сбрасывает само сохранение — метки всех таблиц, которые оно
/// тронуло (перехватчик К4): club-summary и публичные выдачи денормализуют клуб.
/// </summary>
/// <param name="clubSync">
/// Пересборка составов групп, подписанных на канонические клубы, после склейки. Необязательна:
/// тестам склейки она не нужна; в приложении её подставляет DI.
/// </param>
public class ClubMergeService(
    SwimmDbContext db, IClubStandingService standings,
    IHubGroupClubSubscriptionService? clubSync = null)
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

        // Зачётные единицы, где у ДУБЛЕЙ есть строки в материализованном зачёте. Собираем
        // ДО переноса результатов: после него связь «клуб → соревнование» уже не найти,
        // а пересчёта только по соревнованиям канона оказалось мало (2026-08-01: после
        // склейки 68 дублей три строки исчезнувших клубов пережили merge).
        var standingUnits = new HashSet<int>();

        foreach (var pair in pairs)
        {
            var res = new ClubMergePairResult
            {
                CanonicalId = pair.CanonicalId,
                DuplicateId = pair.DuplicateId,
                Kind = pair.IsRename ? ClubMergeKinds.Rename : ClubMergeKinds.Duplicate,
            };
            report.Pairs.Add(res);

            if (!ClubMergeKinds.IsKnown(pair.Kind))
            {
                res.Status = "error";
                res.Conflicts.Add($"неизвестный вид склейки «{pair.Kind}» (duplicate | rename)");
                continue;
            }

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

            // Первой строкой — кто в кого: пару, добавленную руками по Id, админ видит по именам.
            res.Actions.Add($"«{duplicate.Name}» #{duplicate.Id} → «{canonical.Name}» #{canonical.Id}"
                + (pair.IsRename ? " — переименование" : " — дубль"));

            // Р113: оба клуба в одних протоколах — предупреждение второй строкой (не конфликт:
            // решает человек, бывает и один клуб двумя написаниями в одном протоколе).
            var overlap = (await ClubProtocolOverlapQuery.LoadAsync(db, [(canonical.Id, duplicate.Id)], ct))
                [(canonical.Id, duplicate.Id)];
            if (overlap.Meets > 0)
                res.Actions.Add(ClubPairOverlapRule.Warning(overlap));

            foreach (var unitId in await db.ClubCompetitionStandings.AsNoTracking()
                         .Where(s => s.ClubId == duplicate.Id)
                         .Select(s => s.CompetitionId)
                         .ToListAsync(ct))
                standingUnits.Add(unitId);

            var results = await db.Results.Where(r => r.ClubId == duplicate.Id).ToListAsync(ct);

            // Переименование (Н2, Н3): старое название — в историю канона, с годами, когда под
            // ним выступали. Даты — по строкам дубля ДО переноса: после него строки уже не
            // отличить от строк канона. Строки, напечатанные под этим названием, его помнят;
            // строка, уже помнящая ещё более старое название (дубль сам был переименован),
            // остаётся при своём — оно точнее.
            if (pair.IsRename)
            {
                var formerName = new ClubName
                {
                    Club = canonical,
                    ClubId = canonical.Id,
                    Name = duplicate.Name,
                    NameEn = duplicate.NameEn,
                    UsedFrom = results.Count > 0 ? results.Min(r => r.CompetitionDate) : null,
                    UsedTo = results.Count > 0 ? results.Max(r => r.CompetitionDate) : null,
                    Kind = ClubNameKinds.Former,
                    MergedClubId = duplicate.Id,
                };
                db.ClubNames.Add(formerName);
                var marked = 0;
                foreach (var r in results.Where(r => r.ClubNameId == null))
                {
                    r.ClubName = formerName;
                    marked++;
                }
                res.Actions.Add(
                    $"ClubNames: прежнее название «{duplicate.Name}»" +
                    (formerName.UsedFrom is DateTime from && formerName.UsedTo is DateTime to
                        ? $" ({from:dd.MM.yyyy} – {to:dd.MM.yyyy})" : " (стартов нет)"));
                if (marked > 0) res.Actions.Add($"Results.ClubNameId: {marked} строк помнят прежнее название");
            }

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

            // Подписки групп на клуб: подписка на дубль следит за пустым клубом. Уникальность —
            // по группе, не по клубу, поэтому перевесить можно всегда; состав пересоберётся
            // после SaveChanges (канон теперь несёт и пловцов дубля).
            var subscriptions = await db.HubGroupClubSubscriptions.Where(s => s.ClubId == duplicate.Id).ToListAsync(ct);
            foreach (var s in subscriptions) s.ClubId = canonical.Id;
            Note(res, "HubGroupClubSubscriptions", subscriptions.Count);

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

            // Без цепочек (Н4): клубы, склеенные в дубль раньше, теперь смотрят прямо на канон.
            // Иначе /clubs/{старый id}, импорт и все выборки «MergedIntoId ?? Id» упирались бы
            // в пустое надгробие (1329 → 605 → 1372 открывал пустой 605).
            var tombstones = await db.Clubs.Where(c => c.MergedIntoId == duplicate.Id).ToListAsync(ct);
            foreach (var t in tombstones) t.MergedIntoId = canonical.Id;
            if (tombstones.Count > 0)
                res.Actions.Add($"Clubs.MergedIntoId: {tombstones.Count} склеенных в дубль → canonical ({string.Join(", ", tombstones.Select(t => $"#{t.Id}"))})");

            // Прежние названия дубля переезжают к канону вместе с его строками.
            var names = await db.ClubNames.Where(n => n.ClubId == duplicate.Id).ToListAsync(ct);
            foreach (var n in names) n.ClubId = canonical.Id;
            Note(res, "ClubNames", names.Count);

            // Кандидаты смены клуба (Р98) смотрят на клуб по Id без FK. «Было» и «стало» на дубле
            // переписываем на канон; совпали — смены больше нет (карточка уже на каноне), кандидат
            // снимается. 19.11.2026: после склейки 1246→441 у ג׳ואי לווי висел кандидат
            // «Maccabi Rishon Seals → מכבי ראשון לציון SEALS» при карточке уже на 441.
            // Журнал Sys_SwimmerClubChanges не трогаем: это история, надгробие хранит название (Р101).
            var candidates = await db.SwimmerClubCandidates
                .Where(c => c.FromClubId == duplicate.Id || c.ToClubId == duplicate.Id).ToListAsync(ct);
            int movedCandidates = 0, droppedCandidates = 0;
            foreach (var c in candidates)
            {
                if (db.Entry(c).State == EntityState.Deleted) continue; // снят предыдущей парой этого вызова
                if (c.FromClubId == duplicate.Id) c.FromClubId = canonical.Id;
                if (c.ToClubId == duplicate.Id) c.ToClubId = canonical.Id;
                if (c.FromClubId == c.ToClubId)
                {
                    db.SwimmerClubCandidates.Remove(c);
                    droppedCandidates++;
                }
                else movedCandidates++;
            }
            Note(res, "Sys_SwimmerClubCandidates", movedCandidates);
            if (droppedCandidates > 0)
                res.Actions.Add($"Sys_SwimmerClubCandidates: {droppedCandidates} снято (было = стало после склейки)");

            // Дозаполнение пустых полей канонического из дубля. Кросс-скриптовая пара:
            // канон — ивритская запись, латинское название дубля уходит в NameEn.
            // При переименовании — нет: английское название дубля это СТАРОЕ название.
            if (!pair.IsRename && string.IsNullOrEmpty(canonical.NameEn))
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

        // Материализованный клубный зачёт помнит места ИСЧЕЗНУВШЕГО клуба — пересчитываем.
        // ДВА прохода, и оба нужны:
        //   1) зачёты, где стоял сам дубль (собраны выше, до переноса) — иначе его место
        //      остаётся висеть в таблице;
        //   2) соревнования канона — у него изменились очки и состав.
        if (anyMerged)
        {
            foreach (var unitId in standingUnits)
                await standings.RebuildForCompetitionAsync(unitId, ct);

            foreach (var id in report.Pairs.Where(p => p.Status == "merged").Select(p => p.CanonicalId).Distinct())
                await standings.RebuildForClubAsync(id, ct);

            // Группы, подписанные на канон (в т.ч. перевешенные с дубля), получают пловцов
            // обоих. Склейка уже закоммичена — сбой пересборки её не откатывает, он только
            // пишется в отчёт; догнать можно `dotnet run -- --hubgroup-club-sync`.
            if (clubSync is not null)
            {
                var mergedPairs = report.Pairs.Where(p => p.Status == "merged").ToList();
                try
                {
                    db.ChangeTracker.Clear();
                    var sync = await clubSync.SyncClubsAsync(mergedPairs.Select(p => p.CanonicalId).Distinct().ToList());
                    if (sync.Groups > 0)
                        mergedPairs[0].Actions.Add(
                            $"HubGroupClubSubscriptions: пересобрано групп {sync.Groups}, пловцов +{sync.Added} / −{sync.Removed}");
                }
                catch (Exception ex)
                {
                    mergedPairs[0].Actions.Add(
                        $"HubGroupClubSubscriptions: пересборка не выполнена ({ex.GetType().Name}: {ex.Message}) — запустить --hubgroup-club-sync");
                }
            }
        }

        return report;
    }

    public async Task<ClubChainFlattenReport> FlattenChainsAsync(bool dryRun = true, CancellationToken ct = default)
    {
        var report = new ClubChainFlattenReport { DryRun = dryRun };

        var mergedInto = await db.Clubs.AsNoTracking()
            .Select(c => new { c.Id, c.MergedIntoId })
            .ToDictionaryAsync(c => c.Id, c => c.MergedIntoId, ct);
        var plan = ClubMergeChains.Plan(mergedInto);
        report.Broken.AddRange(plan.Broken);

        var moveIds = plan.Moves.Select(m => m.ClubId).ToList();
        var tracked = await db.Clubs.Where(c => moveIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        foreach (var move in plan.Moves)
        {
            tracked[move.ClubId].MergedIntoId = move.To;
            report.Moves.Add($"#{move.ClubId}: {move.From} → {move.To}");
        }

        // Прежние названия, чей хозяин — надгробие (заведены до склейки хозяина), — к живому клубу.
        var tombstoneIds = mergedInto.Where(kv => kv.Value != null).Select(kv => kv.Key).ToList();
        foreach (var n in await db.ClubNames.Where(n => tombstoneIds.Contains(n.ClubId)).ToListAsync(ct))
        {
            if (ClubMergeChains.FinalOf(n.ClubId, mergedInto) is not int live) continue;
            report.NameMoves.Add($"ClubNames #{n.Id} «{n.Name}»: {n.ClubId} → {live}");
            n.ClubId = live;
        }

        if (dryRun) db.ChangeTracker.Clear();
        else await db.SaveChangesAsync(ct);
        return report;
    }

    private static bool IsSynthetic(Club c) => c.Name.StartsWith("SYNTH", StringComparison.Ordinal);

    private static bool HasHebrew(string s) => s.Any(c => c is >= 'א' and <= 'ת');

    private static void Note(ClubMergePairResult res, string table, int count)
    {
        if (count > 0) res.Actions.Add($"{table}: {count} → canonical");
    }
}
