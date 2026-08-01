using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Качество справочника рекордов: сверка с нашими протоколами + реестр спорных записей
/// (docs/plans/records-quality-plan.md).
///
/// ⚠ Ошибки источника мы не чиним — только помечаем. И отдельно: «заплыв не найден» не
/// равно «ошибка»: протоколы загружены не за все годы.
/// </summary>
public interface IRecordQualityService
{
    /// <summary>
    /// Пересчитывает сверку для ВСЕХ рекордов: ищет в Results заплыв с тем же временем на
    /// той же оси (стиль × дистанция × бассейн × пол) и пишет результат в
    /// Sys_RecordVerifications.
    /// </summary>
    Task<RecordVerifyResult> VerifyAllAsync(CancellationToken ct = default);

    /// <summary>Сводка для дашборда: сверка + счётчики реестра + последние открытые претензии.</summary>
    Task<RecordQualitySummary> GetSummaryAsync(int issuesLimit = 20, CancellationToken ct = default);

    /// <summary>Реестр претензий постранично; <paramref name="status"/> null — все статусы.</summary>
    Task<PagedResult<RecordIssueDto>> ListIssuesAsync(
        string? status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Заводит претензию. Повторное заведение той же оси с тем же временем — не ошибка:
    /// обновляет существующую запись (обоснование/причина), чтобы не плодить дубли.
    /// </summary>
    Task<RecordIssueDto> CreateIssueAsync(
        RecordIssueInputDto input, string createdBy, CancellationToken ct = default);

    /// <summary>Меняет статус/обоснование/причину. null — вернуть, если записи нет.</summary>
    Task<RecordIssueDto?> UpdateIssueAsync(
        int id, RecordIssueUpdateDto update, CancellationToken ct = default);

    /// <summary>Удаляет запись реестра. false — записи не было.</summary>
    Task<bool> DeleteIssueAsync(int id, CancellationToken ct = default);
}
