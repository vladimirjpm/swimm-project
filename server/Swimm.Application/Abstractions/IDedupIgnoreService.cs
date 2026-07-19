using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// «Развязка» пар дедупа: админ помечает пару (пловцов или клубов) как «не дубли»,
/// и она больше не всплывает в кандидатах на склейку. Пары нормализуются
/// (меньший Id первым), порядок аргументов не важен.
/// </summary>
public interface IDedupIgnoreService
{
    /// <param name="entityType">swimmer | club</param>
    Task AddAsync(string entityType, int idA, int idB, CancellationToken ct = default);

    /// <returns>false — такой пары в списке не было.</returns>
    Task<bool> RemoveAsync(string entityType, int idA, int idB, CancellationToken ct = default);

    /// <summary>Список развязанных пар с именами (для UI «вернуть в кандидаты»).</summary>
    Task<IReadOnlyList<DedupIgnoredPairDto>> ListAsync(string entityType, CancellationToken ct = default);
}
