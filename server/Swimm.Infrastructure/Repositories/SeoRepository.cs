using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Факты для серверных заголовков страниц и sitemap.xml (A10). Read-only контекст (swimm_ro):
/// всё здесь публично. Кэш — как у остальных ответов витрины: метки таблиц собираются сами
/// (К3), правка пловца/клуба/соревнования сбрасывает свою запись.
/// </summary>
public class SeoRepository : ISeoRepository
{
    private readonly SwimmReadDbContext _db;
    private readonly ICacheService _cache;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    public SeoRepository(SwimmReadDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public Task<SeoSwimmerFacts?> GetSwimmerAsync(int id, CancellationToken ct = default) =>
        _cache.GetOrCreateAsync<SeoSwimmerFacts?>($"seo:swimmer:{id}", async () =>
        {
            // Пловец закрытой группы (Р71) для витрины не существует — его имя в HTML не попадёт.
            var s = await _db.Swimmers.AsNoTracking()
                .Where(x => x.Id == id && x.PrivateHubGroupId == null)
                .Select(x => new
                {
                    x.Id, x.FirstName, x.LastName, x.FirstNameEn, x.LastNameEn,
                    ClubName = x.Club != null ? x.Club.Name : null,
                    ClubNameEn = x.Club != null ? x.Club.NameEn : null,
                    x.HideFromSearch, x.BirthYear,
                })
                .FirstOrDefaultAsync(ct);
            if (s is null) return null;

            var results = _db.Results.AsNoTracking().Where(r => r.SwimmerId == id);
            var swims = await results.CountAsync(ct);
            var competitions = swims == 0 ? 0 : await results.Select(r => r.CompetitionId).Distinct().CountAsync(ct);

            return new SeoSwimmerFacts(s.Id, s.FirstName, s.LastName, s.FirstNameEn, s.LastNameEn,
                s.ClubName, s.ClubNameEn, s.HideFromSearch, s.BirthYear, swims, competitions);
        }, CacheTtl);

    public Task<SeoClubFacts?> GetClubAsync(int id, CancellationToken ct = default) =>
        _cache.GetOrCreateAsync<SeoClubFacts?>($"seo:club:{id}", async () =>
        {
            var c = await _db.Clubs.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.Id, x.Name, x.NameEn, x.IsPseudo, x.MergedIntoId })
                .FirstOrDefaultAsync(ct);
            if (c is null) return null;

            var swimmers = await _db.Swimmers.AsNoTracking()
                .CountAsync(x => x.ClubId == id && x.PrivateHubGroupId == null, ct);

            return new SeoClubFacts(c.Id, c.Name, c.NameEn, c.IsPseudo, c.MergedIntoId, swimmers);
        }, CacheTtl);

    public Task<SeoCompetitionFacts?> GetCompetitionAsync(int id, CancellationToken ct = default) =>
        _cache.GetOrCreateAsync<SeoCompetitionFacts?>($"seo:competition:{id}", async () =>
        {
            var c = await _db.Competitions.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.Id, x.Name, x.SubName, x.Date, x.PoolType })
                .FirstOrDefaultAsync(ct);
            if (c is null) return null;

            var results = _db.Results.AsNoTracking().Where(r => r.CompetitionId == id);
            var swims = await results.CountAsync(ct);
            var swimmers = swims == 0 ? 0 : await results.Select(r => r.SwimmerId).Distinct().CountAsync(ct);

            return new SeoCompetitionFacts(c.Id, c.Name, c.SubName, c.Date, c.PoolType, swims, swimmers);
        }, CacheTtl);

    public Task<IReadOnlyList<string>> GetSitemapPathsAsync(CancellationToken ct = default) =>
        _cache.GetOrCreateAsync<IReadOnlyList<string>>("seo:sitemap", async () =>
        {
            // Соревнования — все: у каждого есть страница результатов.
            var competitions = await _db.Competitions.AsNoTracking()
                .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);

            // Клубы — живые: без псевдоклубов и без склеенных (их canonical — у того, в кого склеены).
            var clubs = await _db.Clubs.AsNoTracking()
                .Where(x => !x.IsPseudo && x.MergedIntoId == null)
                .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);

            // Пловцы — с результатами, наверняка взрослые (страницы детей не индексируются —
            // SeoText.IsCertainlyAdult), не скрытые из поиска, не пловцы закрытых групп.
            var latestAdultBirthYear = SeoText.LatestAdultBirthYear(IsraelTime.ToLocal(DateTime.UtcNow).Year);
            var swimmers = await _db.Swimmers.AsNoTracking()
                .Where(x => !x.HideFromSearch && x.PrivateHubGroupId == null
                            && x.BirthYear > 0 && x.BirthYear <= latestAdultBirthYear
                            && _db.Results.Any(r => r.SwimmerId == x.Id))
                .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);

            var paths = new List<string>(competitions.Count + clubs.Count + swimmers.Count);
            paths.AddRange(competitions.Select(id => $"/competitions/{id}"));
            paths.AddRange(clubs.Select(id => $"/clubs/{id}"));
            paths.AddRange(swimmers.Select(id => $"/swimmers/{id}"));
            return paths;
        }, CacheTtl);
}
