using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

public class CategoryRepository : ICategoryRepository
{
    // Read-only контекст (swimm_ro) — категории публичны.
    private readonly SwimmReadDbContext _db;
    private readonly ICacheService _cache;

    private const string AllCacheKey = "categories:all";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public CategoryRepository(SwimmReadDbContext db, ICacheService cache)
    {
        _db    = db;
        _cache = cache;
    }

    // GetOrCreate, а не Get + Set: запись собирается в своём контексте и получает метки своих
    // таблиц сама (К3, docs/plans/cache-tags-plan.md) — от кого бы её ни позвали.
    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync() =>
        _cache.GetOrCreateAsync(AllCacheKey, LoadCategoriesAsync, CacheTtl);

    public Task<CategoryDetailDto?> GetByKeyAsync(string key) =>
        _cache.GetOrCreateAsync($"categories:{key}", () => LoadByKeyAsync(key), CacheTtl);

    private async Task<IReadOnlyList<CategoryDto>> LoadCategoriesAsync()
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new CategoryDto
            {
                Key          = c.Key,
                Name         = c.Name,
                NameHe       = c.NameHe,
                Badge        = c.Badge,
                DisplayOrder = c.DisplayOrder,
                MinAge       = c.MinAge,
                MaxAge       = c.MaxAge
            })
            .ToListAsync();

        return categories;
    }

    private async Task<CategoryDetailDto?> LoadByKeyAsync(string key)
    {
        var category = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Key == key)
            .FirstOrDefaultAsync();

        if (category is null)
            return null;

        var competitions = await _db.CategoryCompetitions
            .AsNoTracking()
            .Where(cc => cc.CategoryId == category.Id)
            .OrderBy(cc => cc.DisplayOrder)
            .Join(
                _db.Competitions,
                cc => cc.CompetitionId,
                comp => comp.Id,
                (cc, comp) => new CategoryCompetitionDto
                {
                    Id                    = comp.Id,
                    Name                  = comp.Name,
                    Date                  = comp.Date,
                    PoolType              = comp.PoolType,
                    IsMasters             = comp.IsMasters,
                    IsAward               = comp.IsAward,
                    ShowCombineAllResults = comp.ShowCombineAllResults,
                    DisplayOrder          = cc.DisplayOrder
                })
            .ToListAsync();

        var dto = new CategoryDetailDto
        {
            Key          = category.Key,
            Name         = category.Name,
            NameHe       = category.NameHe,
            Badge        = category.Badge,
            DisplayOrder = category.DisplayOrder,
            Competitions = competitions
        };

        return dto;
    }
}
