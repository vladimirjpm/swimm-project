using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Скрыть пловца из поиска» по просьбе (A10 шаг 1). Пишет флаг <c>Swimmers.HideFromSearch</c>
/// через EF — сброс кэша (страница пловца, sitemap) идёт сам по метке таблицы (К4).
/// </summary>
public class SearchVisibilityService : ISearchVisibilityService
{
    private readonly SwimmDbContext _db;

    public SearchVisibilityService(SwimmDbContext db) => _db = db;

    public async Task<IReadOnlyList<SearchHiddenSwimmerDto>> ListHiddenAsync(CancellationToken ct = default)
    {
        var rows = await _db.Swimmers.AsNoTracking()
            .Where(s => s.HideFromSearch)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s => new
            {
                s.Id, s.FirstName, s.LastName, s.FirstNameEn, s.LastNameEn, s.BirthYear,
                Club = s.Club != null ? s.Club.Name : null,
            })
            .ToListAsync(ct);

        return rows.Select(s =>
        {
            var he = $"{s.FirstName} {s.LastName}".Trim();
            var name = he.Length > 0 ? he : $"{s.FirstNameEn} {s.LastNameEn}".Trim();
            return new SearchHiddenSwimmerDto(s.Id, name, s.BirthYear, s.Club);
        }).ToList();
    }

    public async Task<bool> SetHiddenAsync(int swimmerId, bool hidden, CancellationToken ct = default)
    {
        var swimmer = await _db.Swimmers.FirstOrDefaultAsync(s => s.Id == swimmerId, ct);
        if (swimmer is null || swimmer.PrivateHubGroupId != null) return false;
        if (swimmer.HideFromSearch == hidden) return true;

        swimmer.HideFromSearch = hidden;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
