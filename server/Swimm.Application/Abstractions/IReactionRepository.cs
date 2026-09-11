using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Реакции: ❤ на медиа и 🎉 на заплыв. Идемпотентные тогглы —
/// повторный POST/DELETE не меняет состояние, всегда возвращают итог.
/// </summary>
public interface IReactionRepository
{
    /// <summary>
    /// null — медиа не найдено или не видно этому пользователю (своё либо одобренная
    /// публикация по общему правилу аудитории — та же, что у протокола и страницы пловца).
    /// </summary>
    Task<ReactionStateDto?> SetLikeAsync(int userId, int mediaId, bool on, bool isSiteAdmin);

    /// <summary>null — заплыв не найден. Результаты публичны — поздравить может любой залогиненный.</summary>
    Task<ReactionStateDto?> SetCheerAsync(int userId, long resultId, bool on);
}
