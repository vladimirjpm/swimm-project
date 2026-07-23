using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Админский CRUD стилей (см. <see cref="IStyleAdminRepository"/>). Пишет через owner-контекст;
/// имена стилей денормализованы в публичных выдачах результатов, поэтому после мутаций
/// сбрасывает кэш целиком. Зарезервированные стили (посевные 7) защищены от rename/delete.
/// </summary>
public class StyleAdminRepository(SwimmDbContext db, ICacheService cache) : IStyleAdminRepository
{
    public async Task<IReadOnlyList<StyleAdminRowDto>> GetAllAsync()
    {
        var counts = await db.Results.AsNoTracking()
            .GroupBy(r => r.StyleId)
            .Select(g => new { StyleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StyleId, x => x.Count);

        var styles = await db.Styles.AsNoTracking().OrderBy(s => s.Name).ToListAsync();

        return styles.Select(s => new StyleAdminRowDto
        {
            Id = s.Id,
            Name = s.Name,
            ResultCount = counts.GetValueOrDefault(s.Id),
            IsReserved = Style.ReservedNames.Contains(s.Name)
        }).ToList();
    }

    public async Task<StyleEditDto?> GetByIdAsync(int id)
    {
        var s = await db.Styles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return null;

        var count = await db.Results.AsNoTracking().CountAsync(r => r.StyleId == id);
        return new StyleEditDto
        {
            Id = s.Id,
            Name = s.Name,
            ResultCount = count,
            IsReserved = Style.ReservedNames.Contains(s.Name)
        };
    }

    public async Task<StyleSaveResult> CreateAsync(StyleInputDto input)
    {
        var error = await ValidateAsync(input, excludeId: null);
        if (error != null) return StyleSaveResult.Fail(error);

        var style = new Style { Name = input.Name.Trim() };
        db.Styles.Add(style);
        return await SaveAsync(style);
    }

    public async Task<StyleSaveResult> UpdateAsync(int id, StyleInputDto input)
    {
        var style = await db.Styles.FindAsync(id);
        if (style == null) return StyleSaveResult.Fail($"Стиль #{id} не найден");

        // Имя посевного стиля зашито в парсер/импорт/рекорды — переименование запрещаем.
        var name = (input.Name ?? "").Trim();
        if (Style.ReservedNames.Contains(style.Name) && !string.Equals(style.Name, name, StringComparison.Ordinal))
            return StyleSaveResult.Fail(
                $"Стиль «{style.Name}» зарезервирован в коде — переименование запрещено.");

        var error = await ValidateAsync(input, excludeId: id);
        if (error != null) return StyleSaveResult.Fail(error);

        style.Name = name;
        return await SaveAsync(style);
    }

    public async Task<StyleSaveResult> DeleteAsync(int id)
    {
        var style = await db.Styles.FindAsync(id);
        if (style == null) return StyleSaveResult.Fail($"Стиль #{id} не найден");

        if (Style.ReservedNames.Contains(style.Name))
            return StyleSaveResult.Fail($"Стиль «{style.Name}» зарезервирован в коде — удаление запрещено.");

        var count = await db.Results.CountAsync(r => r.StyleId == id);
        if (count > 0)
            return StyleSaveResult.Fail(
                $"На стиль ссылаются результаты: {count}. Удаление недоступно, пока есть заплывы этого стиля.");

        db.Styles.Remove(style);
        await db.SaveChangesAsync();
        await cache.InvalidateAllAsync();
        return StyleSaveResult.Ok(id);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<StyleSaveResult> SaveAsync(Style style)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return StyleSaveResult.Fail("Не удалось сохранить: имя уже занято другим стилем.");
        }
        await cache.InvalidateAllAsync();
        return StyleSaveResult.Ok(style.Id);
    }

    private async Task<string?> ValidateAsync(StyleInputDto input, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "Имя обязательно";

        var name = input.Name.Trim();
        var dup = await db.Styles.AnyAsync(s => s.Id != excludeId && s.Name == name);
        if (dup) return $"Имя «{name}» уже занято другим стилем";

        return null;
    }
}
