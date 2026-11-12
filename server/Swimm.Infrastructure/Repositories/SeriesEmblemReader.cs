using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Значок серии для публичного адреса (см. <see cref="ISeriesEmblemReader"/>), read-only контекст
/// (роль swimm_ro). Серверного кэша нет намеренно: ответ неизменяем и живёт в кэше браузера, а
/// запрос — одна строка по первичному ключу.
/// </summary>
public class SeriesEmblemReader(SwimmReadDbContext db) : ISeriesEmblemReader
{
    public Task<SeriesEmblemFileDto?> GetAsync(int id, string sha256, CancellationToken ct = default) =>
        db.SeriesEmblems
            .Where(e => e.Id == id && e.Sha256 == sha256)
            .Select(e => new SeriesEmblemFileDto(e.Data, e.ContentType))
            .FirstOrDefaultAsync(ct);
}
