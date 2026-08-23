using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Отладочные подробности: общий тумблер (<c>DebugDetails</c> в /Admin/Settings) + частные
/// опции из <c>Sys_DebugOptions</c>.
///
/// Таблица досеивается известными ключами при первом обращении: новый ключ появляется в коде
/// (<see cref="DebugOptionKeys.All"/>), а не отдельной миграцией на каждую опцию — иначе
/// каждая мелкая подробность требовала бы миграции.
/// </summary>
public class DebugOptionsService(SwimmDbContext db, ISettingsService settings) : IDebugOptionsService
{
    public bool MasterEnabled => settings.GetValue("DebugDetails", false);

    public async Task<bool> IsEnabledAsync(string key, CancellationToken ct = default)
    {
        // Общий выключен — в БД можно не ходить вовсе: ни одна опция не действует.
        if (!MasterEnabled) return false;

        return await db.DebugOptions.AsNoTracking()
            .Where(o => o.Key == key)
            .Select(o => o.Enabled)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<DebugOptionsDto> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureSeededAsync(ct);

        var master = MasterEnabled;
        var rows = await db.DebugOptions.AsNoTracking().OrderBy(o => o.Key).ToListAsync(ct);

        return new DebugOptionsDto(
            master,
            rows.Select(o => new DebugOptionDto(
                o.Key, o.Title, o.Description, o.Enabled,
                Effective: master && o.Enabled,
                o.UpdatedAt, o.UpdatedBy)).ToList());
    }

    public async Task<bool> SetAsync(string key, bool enabled, string? updatedBy, CancellationToken ct = default)
    {
        await EnsureSeededAsync(ct);

        var row = await db.DebugOptions.FirstOrDefaultAsync(o => o.Key == key, ct);
        if (row == null) return false;

        row.Enabled = enabled;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = updatedBy;

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Досеивает строки для ключей, которых ещё нет. Тексты (Title/Description) при этом
    /// обновляются из кода: они — часть кода, а не данные, которые правит админ.
    /// </summary>
    private async Task EnsureSeededAsync(CancellationToken ct)
    {
        var existing = await db.DebugOptions.ToDictionaryAsync(o => o.Key, ct);
        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var (key, title, description) in DebugOptionKeys.All)
        {
            if (existing.TryGetValue(key, out var row))
            {
                if (row.Title == title && row.Description == description) continue;

                row.Title = title;
                row.Description = description;
                changed = true;
                continue;
            }

            db.DebugOptions.Add(new DebugOption
            {
                Key = key,
                Enabled = false,          // новая подробность по умолчанию молчит
                Title = title,
                Description = description,
                UpdatedAt = now
            });
            changed = true;
        }

        if (changed) await db.SaveChangesAsync(ct);
    }
}
