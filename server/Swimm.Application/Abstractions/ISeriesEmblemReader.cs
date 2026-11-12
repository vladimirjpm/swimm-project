using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Публичная выдача значка серии (docs/plans/series-emblem-plan.md): анонимный read-путь.
/// </summary>
public interface ISeriesEmblemReader
{
    /// <summary>
    /// Картинка по Id и хэшу содержимого. Хэш обязан совпасть: адрес кэшируется навсегда, и под
    /// ним не должно оказаться другое содержимое. null — значка нет или хэш не его.
    /// </summary>
    Task<SeriesEmblemFileDto?> GetAsync(int id, string sha256, CancellationToken ct = default);
}
