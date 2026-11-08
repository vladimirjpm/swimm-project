using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Факты для серверных заголовков страниц, ссылки для их тела в голом HTML и sitemap.xml (A10;
/// тело и lastmod — после отчёта Search Console 01.11.2026). Read-only контекст (swimm_ro):
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

    public Task<IReadOnlyList<SeoSitemapEntry>> GetSitemapAsync(CancellationToken ct = default) =>
        _cache.GetOrCreateAsync<IReadOnlyList<SeoSitemapEntry>>("seo:sitemap", async () =>
        {
            // lastmod — дата последнего старта на странице (Results.CompetitionDate): новое
            // соревнование меняет страницы своих пловцов и клубов, и краулер видит это по дате.

            // Соревнования: однодневные — все; многодневка — ОДНИМ адресом (первый день), lastmod —
            // по всему событию. Адрес любого дня открывает на витрине всё событие
            // (findSourceForCompetitionId на клиенте), так что дни 2…N — дубли одной страницы;
            // раньше sitemap отдавал каждый день, и человек из поиска попадал на день 1
            // чемпионата 6594 — 43 строки из 3968 (08.11.2026).
            var rows = await _db.Competitions.AsNoTracking()
                .Select(x => new
                {
                    x.Id,
                    x.EventId,
                    x.DayNumber,
                    x.Date,
                    Last = _db.Results.Where(r => r.CompetitionId == x.Id).Max(r => (DateTime?)r.CompetitionDate),
                })
                .ToListAsync(ct);
            var competitions = rows
                .GroupBy(x => x.EventId is { } e ? -e : x.Id) // одиночные — сами по себе, дни — по событию
                .Select(g => new
                {
                    Id = CompetitionDays.FirstId(g.Select(x => new CompetitionDays.Day(x.Id, x.DayNumber, x.Date))),
                    Last = g.Max(x => x.Last),
                })
                .OrderBy(x => x.Id)
                .ToList();

            // Клубы — живые: без псевдоклубов и без склеенных (их canonical — у того, в кого склеены).
            var clubs = await LiveClubs()
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, Last = _db.Results.Where(r => r.ClubId == x.Id).Max(r => (DateTime?)r.CompetitionDate) })
                .ToListAsync(ct);

            var swimmers = await IndexableSwimmers()
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, Last = _db.Results.Where(r => r.SwimmerId == x.Id).Max(r => (DateTime?)r.CompetitionDate) })
                .ToListAsync(ct);

            static DateOnly? Day(DateTime? d) => d is { } v ? DateOnly.FromDateTime(v) : null;
            var entries = new List<SeoSitemapEntry>(competitions.Count + clubs.Count + swimmers.Count);
            entries.AddRange(competitions.Select(x => new SeoSitemapEntry($"/competitions/{x.Id}", Day(x.Last))));
            entries.AddRange(clubs.Select(x => new SeoSitemapEntry($"/clubs/{x.Id}", Day(x.Last))));
            entries.AddRange(swimmers.Select(x => new SeoSitemapEntry($"/swimmers/{x.Id}", Day(x.Last))));
            return entries;
        }, CacheTtl);

    public Task<IReadOnlyList<SeoCompetitionRef>> GetCompetitionsAsync(CancellationToken ct = default) =>
        _cache.GetOrCreateAsync<IReadOnlyList<SeoCompetitionRef>>("seo:competitions", async () =>
            await CompetitionRefs(_db.Competitions.AsNoTracking()).ToListAsync(ct), CacheTtl);

    public Task<SeoSwimmerLinks> GetSwimmerLinksAsync(int swimmerId, CancellationToken ct = default) =>
        _cache.GetOrCreateAsync($"seo:links:swimmer:{swimmerId}", async () =>
        {
            var club = await LiveClubs()
                .Where(c => _db.Swimmers.Any(s => s.Id == swimmerId && s.ClubId == c.Id))
                .Select(c => new SeoClubRef(c.Id, c.Name, c.NameEn))
                .FirstOrDefaultAsync(ct);
            var competitions = await CompetitionRefs(_db.Competitions.AsNoTracking()
                    .Where(c => _db.Results.Any(r => r.SwimmerId == swimmerId && r.CompetitionId == c.Id)))
                .ToListAsync(ct);
            return new SeoSwimmerLinks(club, competitions);
        }, CacheTtl);

    public Task<SeoClubLinks> GetClubLinksAsync(int clubId, CancellationToken ct = default) =>
        _cache.GetOrCreateAsync($"seo:links:club:{clubId}", async () =>
        {
            var swimmers = await SwimmerRefs(IndexableSwimmers().Where(s => s.ClubId == clubId)).ToListAsync(ct);
            var competitions = await CompetitionRefs(_db.Competitions.AsNoTracking()
                    .Where(c => _db.Results.Any(r => r.ClubId == clubId && r.CompetitionId == c.Id)))
                .ToListAsync(ct);
            return new SeoClubLinks(swimmers, competitions);
        }, CacheTtl);

    public Task<SeoCompetitionLinks> GetCompetitionLinksAsync(int competitionId, CancellationToken ct = default) =>
        _cache.GetOrCreateAsync($"seo:links:competition:{competitionId}", async () =>
        {
            var clubs = await LiveClubs()
                .Where(c => _db.Results.Any(r => r.CompetitionId == competitionId && r.ClubId == c.Id))
                .Select(c => new SeoClubRef(c.Id, c.Name, c.NameEn))
                .ToListAsync(ct);
            var swimmers = await SwimmerRefs(IndexableSwimmers()
                    .Where(s => _db.Results.Any(r => r.CompetitionId == competitionId && r.SwimmerId == s.Id)))
                .ToListAsync(ct);
            return new SeoCompetitionLinks(clubs, swimmers);
        }, CacheTtl);

    /// <summary>Клубы с собственной страницей в выдаче: не псевдо и не склеенные.</summary>
    private IQueryable<Club> LiveClubs() =>
        _db.Clubs.AsNoTracking().Where(x => !x.IsPseudo && x.MergedIntoId == null);

    /// <summary>
    /// Пловцы, чьи страницы индексируются: с результатами, наверняка взрослые (страницы детей не
    /// индексируются — SeoText.IsCertainlyAdult), не скрытые из поиска, не пловцы закрытых групп.
    /// Один фильтр и для sitemap, и для ссылок в теле страниц: имя ребёнка в серверный HTML не попадёт.
    /// </summary>
    private IQueryable<Swimmer> IndexableSwimmers()
    {
        var latestAdultBirthYear = SeoText.LatestAdultBirthYear(IsraelTime.ToLocal(DateTime.UtcNow).Year);
        return _db.Swimmers.AsNoTracking()
            .Where(x => !x.HideFromSearch && x.PrivateHubGroupId == null
                        && x.BirthYear > 0 && x.BirthYear <= latestAdultBirthYear
                        && _db.Results.Any(r => r.SwimmerId == x.Id));
    }

    private static IQueryable<SeoCompetitionRef> CompetitionRefs(IQueryable<Competition> q) =>
        q.Select(c => new SeoCompetitionRef(c.Id, c.Name, c.SubName, c.Date));

    private static IQueryable<SeoSwimmerRef> SwimmerRefs(IQueryable<Swimmer> q) =>
        q.Select(s => new SeoSwimmerRef(s.Id, s.FirstName, s.LastName, s.FirstNameEn, s.LastNameEn));
}
