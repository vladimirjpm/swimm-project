using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IUserMediaRepository
{
    Task<List<UserMediaDto>> GetForUserAsync(int userId, int? swimmerId = null);

    /// <summary>null = Swimmer с таким id не найден.</summary>
    Task<UserMediaDto?> AddAsync(int userId, AddUserMediaRequest request);

    Task<bool> RemoveAsync(int userId, int mediaId);

    /* Пикер привязки к заплыву (Add link, дизайн-бриф §6.3) — публичные данные результатов. */

    /// <summary>Соревнования, где у пловца есть заплывы (для чипов пикера), новые первыми.</summary>
    Task<List<SwimmerCompetitionBriefDto>> GetSwimmerCompetitionsBriefAsync(int swimmerId);

    /// <summary>Заплывы пловца на соревновании (без эстафет) для выбора привязки.</summary>
    Task<List<SwimmerResultBriefDto>> GetSwimmerResultsBriefAsync(int swimmerId, int competitionId);
}
