using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Админский CRUD категорий (см. <see cref="ICategoryAdminRepository"/>). Пишет через
/// owner-контекст <see cref="SwimmDbContext"/>; после мутаций сбрасывает публичный кэш
/// (<see cref="CategoryRepository"/> кэширует список/детали категорий отдельно).
/// </summary>
public class CategoryAdminRepository : ICategoryAdminRepository
{
    private readonly SwimmDbContext _db;
    private readonly ICacheService _cache;

    public CategoryAdminRepository(SwimmDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<CategoryAdminRowDto>> GetAllAsync()
    {
        var counts = await _db.CategoryCompetitions.AsNoTracking()
            .GroupBy(cc => cc.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        var categories = await _db.Categories.AsNoTracking()
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();

        return categories.Select(c => new CategoryAdminRowDto
        {
            Id = c.Id,
            Key = c.Key,
            Name = c.Name,
            Badge = c.Badge,
            DisplayOrder = c.DisplayOrder,
            CompetitionCount = counts.GetValueOrDefault(c.Id),
            IsReserved = Category.ReservedKeys.Contains(c.Key)
        }).ToList();
    }

    public async Task<CategoryEditDto?> GetByIdAsync(int id)
    {
        var c = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return null;

        var count = await _db.CategoryCompetitions.CountAsync(cc => cc.CategoryId == id);

        return new CategoryEditDto
        {
            Id = c.Id,
            Key = c.Key,
            Name = c.Name,
            Badge = c.Badge,
            DisplayOrder = c.DisplayOrder,
            CompetitionCount = count,
            IsReserved = Category.ReservedKeys.Contains(c.Key)
        };
    }

    public async Task<CategorySaveResult> CreateAsync(CategoryInputDto input)
    {
        var error = await ValidateAsync(input, excludeId: null);
        if (error != null) return CategorySaveResult.Fail(error);

        var cat = new Category();
        Apply(cat, input);
        _db.Categories.Add(cat);
        return await SaveAsync(cat);
    }

    public async Task<CategorySaveResult> UpdateAsync(int id, CategoryInputDto input)
    {
        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return CategorySaveResult.Fail($"Категория #{id} не найдена");

        // Ключ зашит в код (IsMasters, приоритет раздела на клиенте/сервере, legacy-редиректы) —
        // переименование сломает эту логику незаметно, поэтому запрещаем.
        var key = (input.Key ?? "").Trim();
        if (Category.ReservedKeys.Contains(cat.Key) && !string.Equals(cat.Key, key, StringComparison.Ordinal))
            return CategorySaveResult.Fail(
                $"Key «{cat.Key}» зарезервирован в коде — переименование запрещено.");

        var error = await ValidateAsync(input, excludeId: id);
        if (error != null) return CategorySaveResult.Fail(error);

        Apply(cat, input);
        return await SaveAsync(cat);
    }

    public async Task<CategorySaveResult> DeleteAsync(int id)
    {
        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return CategorySaveResult.Fail($"Категория #{id} не найдена");

        if (Category.ReservedKeys.Contains(cat.Key))
            return CategorySaveResult.Fail($"Key «{cat.Key}» зарезервирован в коде — удаление запрещено.");

        var count = await _db.CategoryCompetitions.CountAsync(cc => cc.CategoryId == id);
        if (count > 0)
            return CategorySaveResult.Fail(
                $"В категории есть соревнования: {count}. Сначала снимите категорию у всех соревнований.");

        _db.Categories.Remove(cat);
        await _db.SaveChangesAsync();
        await _cache.InvalidateAllAsync();
        return CategorySaveResult.Ok(id);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static void Apply(Category cat, CategoryInputDto input)
    {
        cat.Key = input.Key.Trim();
        cat.Name = input.Name.Trim();
        cat.Badge = string.IsNullOrWhiteSpace(input.Badge) ? null : input.Badge.Trim();
        cat.DisplayOrder = input.DisplayOrder;
    }

    private async Task<CategorySaveResult> SaveAsync(Category cat)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return CategorySaveResult.Fail("Не удалось сохранить: Key уже занят другой категорией.");
        }
        await _cache.InvalidateAllAsync();
        return CategorySaveResult.Ok(cat.Id);
    }

    private async Task<string?> ValidateAsync(CategoryInputDto input, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(input.Key)) return "Key обязателен";
        if (string.IsNullOrWhiteSpace(input.Name)) return "Название обязательно";

        var key = input.Key.Trim();
        var dupKey = await _db.Categories.AnyAsync(c => c.Id != excludeId && c.Key == key);
        if (dupKey) return $"Key «{key}» уже занят другой категорией";

        return null;
    }
}
