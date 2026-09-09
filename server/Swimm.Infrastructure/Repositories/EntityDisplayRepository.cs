using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Настройки отображения страницы коллектива. Пишет через <see cref="SwimmDbContext"/>
/// (rw-путь): read-контекст только читает.
///
/// Колонка `DisplaySettings` разбирается и собирается ТОЛЬКО через
/// <see cref="EntityDisplaySettings"/> — руками JSON тут не строится, иначе форма разъедется
/// между записью и чтением.
/// </summary>
public class EntityDisplayRepository : IEntityDisplayRepository
{
    private readonly SwimmDbContext _db;

    public EntityDisplayRepository(SwimmDbContext db) => _db = db;

    public async Task<bool> UpdateClubAsync(int clubId, EntityDisplayInputDto input, CancellationToken ct = default)
    {
        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId, ct);
        if (club is null) return false;

        club.CoverImageUrl = Normalize(input.CoverImageUrl);
        club.DisplaySettings = Apply(club.DisplaySettings, input);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateGroupAsync(int hubGroupId, EntityDisplayInputDto input, CancellationToken ct = default)
    {
        var group = await _db.HubGroups.FirstOrDefaultAsync(g => g.Id == hubGroupId, ct);
        if (group is null) return false;

        group.CoverImageUrl = Normalize(input.CoverImageUrl);
        group.DisplaySettings = Apply(group.DisplaySettings, input);
        group.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Пустая строка и пробелы — это «нет ссылки», а не значение.</summary>
    private static string? Normalize(string? url)
        => string.IsNullOrWhiteSpace(url) ? null : url.Trim();

    /// <summary>
    /// Правим ТОЛЬКО ключи, которыми владеет форма, остальное содержимое колонки сохраняем:
    /// ключей со временем станет больше, и чужие настройки затирать нельзя.
    /// </summary>
    private static string Apply(string? current, EntityDisplayInputDto input)
    {
        var settings = EntityDisplaySettings.Parse(current);
        settings.Hero.Show = input.ShowHeroImage;
        settings.Hero.MediaId = input.HeroMediaId;
        return settings.ToJson();
    }
}
