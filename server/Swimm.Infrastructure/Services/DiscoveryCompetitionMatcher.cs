using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Единственное место, где решается «эта Discovery-строка уже импортирована»: матч по дате+
/// нормализованному имени с таблицей Competitions (OrgCompId у PDF-импортов не заполняется —
/// имя+дата единственный шов). Используется и списком /Admin/Discovery (<see cref="CompetitionDiscoveryService"/>),
/// и карточкой дашборда (<see cref="DashboardStatusService"/>) — не должно быть двух копий,
/// которые могут разойтись.
/// </summary>
/// <summary>Совпавшее соревнование: его Id и отображаемое имя.</summary>
internal readonly record struct CompetitionMatch(int CompetitionId, string Name);

internal class DiscoveryCompetitionMatcher(SwimmDbContext db)
{
    /// <summary>Для каждой переданной Discovery-строки возвращает совпавшее Competition
    /// (Id+имя) или null, если совпадения нет. Ключ словаря — Id строки Discovery.</summary>
    public async Task<Dictionary<int, CompetitionMatch?>> MatchAsync(
        IReadOnlyList<DiscoveredCompetition> rows, CancellationToken ct = default)
    {
        // Матч «уже импортировано»: дата дня попадает в [DateStart..DateEnd] и имя совпадает
        // после нормализации ЛИБО имя Discovery начинается с имени соревнования — сайт дописывает
        // суффикс района («…- מחוז צפון»), которого в протоколе/БД нет. Нормализация выкидывает
        // кавычки и бэкслеши: в БД встречаются «ארנה», «"ארנה"» и «\"ארנה\"» (артефакт импорта).
        // Дни в Competitions.Date — строка dd/MM/yyyy.
        var competitions = await db.Competitions
            .AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.Date })
            .ToListAsync(ct);
        var candidates = competitions
            .Select(c => new { c.Id, Key = Normalize(c.Name), c.Name, Date = ParseDdMmYyyy(c.Date) })
            .Where(c => c.Date != null && c.Key.Length > 0)
            .ToList();

        var result = new Dictionary<int, CompetitionMatch?>();
        foreach (var d in rows)
        {
            var dKey = Normalize(d.Name);
            CompetitionMatch? matched = null;
            foreach (var c in candidates)
            {
                if (c.Date < d.DateStart || c.Date > d.DateEnd) continue;
                if (dKey == c.Key || dKey.StartsWith(c.Key, StringComparison.Ordinal))
                {
                    matched = new CompetitionMatch(c.Id, c.Name);
                    break;
                }
            }
            result[d.Id] = matched;
        }
        return result;
    }

    /// <summary>Trim/lower, схлопнуть пробелы, выкинуть кавычки/бэкслеши/гереш-гершаим —
    /// они непоследовательны между сайтом и импортированными именами.</summary>
    internal static string Normalize(string name)
    {
        var cleaned = new string(name.Where(c => c is not ('"' or '\\' or '\'' or '׳' or '״' or '`' or '’' or '“' or '”')).ToArray());
        return string.Join(' ', cleaned.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    private static DateTime? ParseDdMmYyyy(string date)
    {
        var seg = date.Split('/');
        if (seg.Length != 3
            || !int.TryParse(seg[0], out var d) || !int.TryParse(seg[1], out var m) || !int.TryParse(seg[2], out var y))
            return null;
        try { return new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
