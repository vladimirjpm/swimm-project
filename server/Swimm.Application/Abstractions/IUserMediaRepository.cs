using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IUserMediaRepository
{
    Task<List<UserMediaDto>> GetForUserAsync(int userId, int? swimmerId = null);

    /// <summary>null = Swimmer с таким id не найден.</summary>
    Task<UserMediaDto?> AddAsync(int userId, AddUserMediaRequest request);

    /// <summary>Всего медиа у пользователя — для потолка анти-спама в контроллере.</summary>
    Task<int> CountForUserAsync(int userId);

    /// <summary>
    /// true — у юзера уже есть эта ссылка (по MediaUrlValidator.DedupKey) к той же цели:
    /// тот же заплыв, то же соревнование (уровень competition) или тот же пловец (уровень swimmer).
    /// </summary>
    Task<bool> HasSameLinkAsync(int userId, AddUserMediaRequest request);

    Task<bool> RemoveAsync(int userId, int mediaId);

    /* Пикер привязки к заплыву (Add link, дизайн-бриф §6.3) — публичные данные результатов. */

    /// <summary>Соревнования, где у пловца есть заплывы (для чипов пикера), новые первыми.</summary>
    Task<List<SwimmerCompetitionBriefDto>> GetSwimmerCompetitionsBriefAsync(int swimmerId);

    /// <summary>Заплывы пловца на соревновании (без эстафет) для выбора привязки.</summary>
    Task<List<SwimmerResultBriefDto>> GetSwimmerResultsBriefAsync(int swimmerId, int competitionId);
}
