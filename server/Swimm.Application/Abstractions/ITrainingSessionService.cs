using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Тренировки группы как записи — завести кнопкой (и без времён: «подводная съёмка»), поправить,
/// удалить пустую, список с медиа (docs/plans/group-events-media-plan.md, Е1). Правила ввода —
/// <c>TrainingSessionRules</c>.
///
/// Права решает контроллер: читать — управляющим и активным участникам, менять — управляющим
/// (<c>HubGroupPermissions.CanEdit</c>). <paramref name="todayLocal"/> — «сегодня» по Израилю; не
/// задан — текущее (задают тесты).
/// </summary>
public interface ITrainingSessionService
{
    /// <summary>Все тренировки группы, новые сверху, с медиа; флаги «моё / могу удалить» — для зрителя.</summary>
    Task<TrainingSessionListDto> ListAsync(int hubGroupId, int viewerUserId, bool isManager);

    Task<TrainingSessionSaveResult> CreateAsync(int hubGroupId, int actorUserId, TrainingSessionInputDto input,
        DateOnly? todayLocal = null);

    /// <summary>У тренировки с временами дату и бассейн не поменять — времена принадлежат тому дню.</summary>
    Task<TrainingSessionSaveResult> UpdateAsync(int hubGroupId, int sessionId, int actorUserId,
        TrainingSessionInputDto input, DateOnly? todayLocal = null);

    /// <summary>
    /// Ссылка к тренировке (Е2): управляющий — всегда, участник — если группа не поставила
    /// «только тренер». Потолки — <c>TrainingSessionRules.MaxMediaPerSession</c> /
    /// <c>MaxMediaPerMember</c>.
    /// </summary>
    Task<TrainingSessionMediaSaveResult> AddMediaAsync(int hubGroupId, int sessionId, int actorUserId, bool isManager,
        TrainingSessionMediaInputDto input);

    /// <summary>Удалить ссылку тренировки: автор — свою, управляющий — любую. Error null — удалено.</summary>
    Task<(int Status, string? Error)> DeleteMediaAsync(int hubGroupId, int sessionId, int mediaId, int actorUserId,
        bool isManager);

    /// <summary>
    /// «Sets &amp; times» тренировки (Т1а): сеты → повторы с уровнем и флагом «моя строка». null —
    /// тренировки нет в этой группе.
    /// </summary>
    Task<TrainingSessionSetsDto?> GetSetsAsync(int hubGroupId, int sessionId, int viewerUserId, bool isManager);

    /// <summary>
    /// ★ ключевого сета (Р4) — по нему строка «You». Сет должен быть в тренировке; null снимает
    /// пометку. Только управляющий (решает контроллер).
    /// </summary>
    Task<TrainingSessionSaveResult> SetKeySetAsync(int hubGroupId, int sessionId, int actorUserId,
        TrainingKeySetInputDto input);

    /// <summary>
    /// Зритель открыл ленту Trainings — медиа других до этого момента больше не «новое» (Т1б).
    /// </summary>
    Task MarkSeenAsync(int hubGroupId, int viewerUserId);

    /// <summary>Удалить можно только тренировку БЕЗ времён; её медиа уходит с ней. Error null — удалено, иначе код и текст отказа.</summary>
    Task<(int Status, string? Error)> DeleteAsync(int hubGroupId, int sessionId);
}
