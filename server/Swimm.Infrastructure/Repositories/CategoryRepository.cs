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

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync()
    {
        var cached = await _cache.GetAsync<IReadOnlyList<CategoryDto>>(AllCacheKey);
        if (cached is not null)
            return cached;

        var categories = await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new CategoryDto
            {
                Key          = c.Key,
                Name         = c.Name,
                NameHe       = c.NameHe,
                Badge        = c.Badge,
                DisplayOrder = c.DisplayOrder
            })
            .ToListAsync();

        await _cache.SetAsync(AllCacheKey, (IReadOnlyList<CategoryDto>)categories, CacheTtl);

        return categories;
    }

    public async Task<CategoryDetailDto?> GetByKeyAsync(string key)
    {
        var cacheKey = $"categories:{key}";

        var cached = await _cache.GetAsync<CategoryDetailDto>(cacheKey);
        if (cached is not null)
            return cached;

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

        await _cache.SetAsync(cacheKey, dto, CacheTtl);

        return dto;
    }
}
