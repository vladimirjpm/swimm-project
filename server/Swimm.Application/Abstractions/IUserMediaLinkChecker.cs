using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// On-demand проверка живости внешних ссылок UserMedia (фаза 7.5 роадмапа). Проверка запускается
/// вручную кнопкой в /Admin/Media — фоновой/периодической проверки нет (см. docs/tasks).
/// </summary>
public interface IUserMediaLinkChecker
{
    /// <summary>Прогоняет все строки Sys_UserMedia, проставляет LinkCheckedAt/LinkOk/LinkStatusCode/LinkError.</summary>
    Task<UserMediaLinkCheckReport> CheckAllAsync(CancellationToken ct = default);

    /// <summary>Список ссылок, помеченных битыми при последней проверке (LinkOk == false).</summary>
    Task<IReadOnlyList<BrokenMediaRowDto>> GetBrokenAsync(CancellationToken ct = default);
}
