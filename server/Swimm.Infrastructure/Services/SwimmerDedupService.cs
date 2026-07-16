using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// C#-порт server/db/dedup-report.sql для админ-UI (фаза 7.2). Реальных пловцов тысячи
/// (не миллионы — синтетика отсекается), так что попарное сравнение внутри BirthYear-групп
/// в памяти дешёвое. Правила «уверенная/спорная» — те же, что в SQL-отчёте:
/// уверенная = dist ≤ 1, год известен, пол не противоречит, клуб совпадает или пуст;
/// спорная = прочее с dist ≤ 1, либо dist = 2 у одноклубников (иначе шум).
/// </summary>
public class SwimmerDedupService(SwimmDbContext db) : ISwimmerDedupService
{
    public async Task<SwimmerDedupReport> FindCandidatesAsync(CancellationToken ct = default)
    {
        var swimmers = await db.Swimmers.AsNoTracking()
            .Where(s => s.SwimmerOrgId == null || !s.SwimmerOrgId.StartsWith("SYNTH-"))
            .Select(s => new
            {
                s.Id, s.LastName, s.FirstName, s.LastNameEn, s.FirstNameEn,
                s.BirthYear, s.Gender, s.Origin,
                Club = s.Club != null ? s.Club.Name : null,
                ResultCount = db.Results.Count(r => r.SwimmerId == s.Id && (r.Note == null || r.Note != "SYNTH"))
            })
            .ToListAsync(ct);

        var report = new SwimmerDedupReport { RealSwimmers = swimmers.Count };

        var prepared = swimmers.Select(s => new
        {
            s.Id, s.BirthYear, s.Gender, s.Club, s.ResultCount,
            Name = $"{s.LastName} {s.FirstName}".Trim(),
            Nh = Normalize($"{s.LastName} {s.FirstName}"),
            NhSwap = Normalize($"{s.FirstName} {s.LastName}"),
            Ne = (s.LastNameEn + s.FirstNameEn).Length > 0 ? Normalize($"{s.LastNameEn} {s.FirstNameEn}") : null
        }).ToList();

        foreach (var group in prepared.GroupBy(s => s.BirthYear))
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
            {
                var a = list[i];
                var b = list[j];
                // Быстрый отсев по длине — как в SQL (abs(len diff) ≤ 2).
                if (Math.Abs(a.Nh.Length - b.Nh.Length) > 2) continue;

                var dist = Math.Min(
                    Levenshtein(a.Nh, b.Nh, 2),
                    Math.Min(
                        Levenshtein(a.Nh, b.NhSwap, 2),
                        a.Ne != null && b.Ne != null ? Levenshtein(a.Ne, b.Ne, 2) : int.MaxValue));
                if (dist > 2) continue;

                var genderOk = a.Gender == b.Gender || a.Gender is null || b.Gender is null;
                var clubOk = a.Club == b.Club || a.Club is null || b.Club is null;
                var sure = dist <= 1 && group.Key != 0 && genderOk && clubOk;
                // Шум: dist=2 у разных клубов — почти всегда разные люди, не показываем.
                if (!sure && dist == 2 && a.Club != b.Club) continue;

                // canonical — у кого больше результатов (при равенстве — меньший Id).
                var (canon, dup) = a.ResultCount >= b.ResultCount ? (a, b) : (b, a);
                report.Candidates.Add(new SwimmerDedupCandidate(
                    canon.Id, canon.Name, canon.Club, canon.ResultCount,
                    dup.Id, dup.Name, dup.Club, dup.ResultCount,
                    group.Key, a.Gender ?? b.Gender, dist, sure));
            }
        }

        // Уверенные сверху, внутри — по расстоянию и имени.
        report.Candidates.Sort((x, y) =>
            (!x.Sure, x.Distance, x.CanonicalName).CompareTo((!y.Sure, y.Distance, y.CanonicalName)));

        // Сироты: вообще без результатов (включая синтетические) и без единой связи.
        var orphanIds = await OrphanQuery()
            .Select(s => new SwimmerOrphan(
                s.Id, (s.LastName + " " + s.FirstName).Trim(), s.BirthYear, s.Origin,
                s.Club != null ? s.Club.Name : null))
            .ToListAsync(ct);
        report.Orphans.AddRange(orphanIds.OrderBy(o => o.Name, StringComparer.Ordinal));

        return report;
    }

    public async Task<SwimmerOrphanCleanupReport> DeleteOrphansAsync(IReadOnlyCollection<int>? ids, CancellationToken ct = default)
    {
        var orphanIds = await OrphanQuery().Select(s => s.Id).ToListAsync(ct);
        var orphanSet = orphanIds.ToHashSet();

        List<int> toDelete;
        List<int> skipped;
        if (ids is null)
        {
            toDelete = orphanIds;
            skipped = [];
        }
        else
        {
            toDelete = ids.Where(orphanSet.Contains).Distinct().ToList();
            skipped = ids.Where(id => !orphanSet.Contains(id)).Distinct().ToList();
        }

        if (toDelete.Count > 0)
        {
            var swimmers = await db.Swimmers.Where(s => toDelete.Contains(s.Id)).ToListAsync(ct);
            db.Swimmers.RemoveRange(swimmers);
            await db.SaveChangesAsync(ct);
        }

        return new SwimmerOrphanCleanupReport(toDelete.Count, toDelete, skipped);
    }

    /// <summary>Критерий сироты — единственное место, где он живёт (Решение 1 в задаче B2).
    /// Ничего не ссылается: ни результаты, ни группы (админ/пользовательские), ни избранное,
    /// ни медиа, ни тренировки, ни привязанный аккаунт. Синтетика (SYNTH-) исключена заранее.</summary>
    private IQueryable<Swimm.Domain.Entities.Swimmer> OrphanQuery() =>
        db.Swimmers.AsNoTracking()
            .Where(s => s.SwimmerOrgId == null || !s.SwimmerOrgId.StartsWith("SYNTH-"))
            .Where(s => !db.Results.Any(r => r.SwimmerId == s.Id)
                && !db.HubGroupMembers.Any(m => m.SwimmerId == s.Id)
                && !db.HubGroupUserMembers.Any(m => m.SwimmerId == s.Id)
                && !db.UserFavorites.Any(f => f.SwimmerId == s.Id)
                && !db.UserMedia.Any(m => m.SwimmerId == s.Id)
                && !db.HubGroupMedia.Any(m => m.SwimmerId == s.Id)
                && !db.TrainingResults.Any(t => t.SwimmerId == s.Id)
                && !db.AppUsers.Any(u => u.SwimmerId == s.Id));

    /// <summary>Нормализация как в dedup-report.sql: trim/lower, финальные ивритские буквы,
    /// гереш ׳/гершаим ״ → ASCII, схлопнуть пробелы.</summary>
    public static string Normalize(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => c switch
        {
            'ך' => 'כ', 'ם' => 'מ', 'ן' => 'נ', 'ף' => 'פ', 'ץ' => 'צ',
            '׳' => '\'', '״' => '"', '’' => '\'',
            _ => c
        });
        return string.Join(' ',
            new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Левенштейн с отсечкой: возвращает max+1, если расстояние больше max.</summary>
    public static int Levenshtein(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, curr[j]);
            }
            if (rowMin > max) return max + 1; // вся строка хуже порога — дальше только растёт
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length] > max ? max + 1 : prev[b.Length];
    }
}
