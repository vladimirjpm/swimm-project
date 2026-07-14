using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IUserMediaRepository
{
    Task<List<UserMediaDto>> GetForUserAsync(int userId, int? swimmerId = null);

    /// <summary>null = Swimmer с таким id не найден.</summary>
    Task<UserMediaDto?> AddAsync(int userId, AddUserMediaRequest request);

    Task<bool> RemoveAsync(int userId, int mediaId);
}
