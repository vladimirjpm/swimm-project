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

    /// <summary>
    /// null — заплыв не найден или поздравлять его нельзя: неявка (NS/DNS — старта не было) или
    /// пловец группы (Р71 — публично его нет). Снять своё 🎉 можно всегда. Результаты публичны —
    /// поздравить может любой залогиненный.
    /// </summary>
    Task<ReactionStateDto?> SetCheerAsync(int userId, long resultId, bool on);

    /// <summary>
    /// 🎉 пачки заплывов: только те, где счётчик больше нуля. <paramref name="userId"/> null — гость,
    /// <c>mine</c> всегда false.
    /// </summary>
    Task<IReadOnlyList<CheerStateDto>> GetCheersAsync(IReadOnlyCollection<long> resultIds, int? userId);
}
