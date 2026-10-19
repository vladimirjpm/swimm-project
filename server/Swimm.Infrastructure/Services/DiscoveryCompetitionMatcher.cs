using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Единственное место, где решается «эта Discovery-строка уже импортирована»: матч по дате+
/// нормализованному имени с таблицей Competitions (OrgCompId у PDF-импортов не заполняется —
/// имя+дата единственный шов). Используется и списком /Admin/Discovery (<see cref="CompetitionDiscoveryService"/>),
/// и карточкой дашборда (<see cref="DashboardStatusService"/>) — не должно быть двух копий,
/// которые могут разойтись. Само правило (нормализация + сравнение) — чистая функция
/// <see cref="DiscoveryNameMatch"/> в Application: его же зовёт календарь главной.
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
        // Правило — DiscoveryNameMatch: дата дня в [DateStart..DateEnd] и имя совпадает после
        // нормализации ЛИБО имя Discovery начинается с имени соревнования (суффикс района).
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
                if (DiscoveryNameMatch.KeysMatch(dKey, c.Key))
                {
                    matched = new CompetitionMatch(c.Id, c.Name);
                    break;
                }
            }
            result[d.Id] = matched;
        }
        return result;
    }

    /// <summary>См. <see cref="DiscoveryNameMatch.Normalize"/>.</summary>
    internal static string Normalize(string name) => DiscoveryNameMatch.Normalize(name);

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
