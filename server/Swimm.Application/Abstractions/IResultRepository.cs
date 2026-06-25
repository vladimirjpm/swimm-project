using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IResultRepository
{
    Task<(List<ResultDto> Items, bool HasMore)> GetPagedAsync(ResultFilter filter, int page, int pageSize);
    Task<ResultDto?> GetByIdAsync(long id);
}
