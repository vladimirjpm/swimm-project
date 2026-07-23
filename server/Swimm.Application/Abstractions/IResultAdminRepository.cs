using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт ручной правки отдельного результата (Admin/Results, фаза 7.2 B). Security-sensitive:
/// меняет публичные данные → реализация валидирует ссылки, сбрасывает кэш, а вызывающий
/// пишет аудит (7.4). Эстафетные строки (RelayId != null) не правятся этой формой.
/// </summary>
public interface IResultAdminRepository
{
    /// <summary>Данные результата для формы. null — не найден или это эстафетная строка.</summary>
    Task<ResultEditDto?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>Применить правку. Ошибка — при неверном времени/несуществующих пловце/клубе.</summary>
    Task<ResultSaveResult> UpdateAsync(long id, ResultEditInputDto input, CancellationToken ct = default);
}
