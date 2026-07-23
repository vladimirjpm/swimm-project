using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Массовый перенос всех результатов одного соревнования в другое (фаза 7.3) — для склейки
/// дублей-соревнований. Обновляет ResultRecord.CompetitionId + денормализованную
/// CompetitionDate на дату цели; эстафеты/галереи едут со своими результатами (ссылки на них
/// на соревнование не завязаны). Источник после переноса пустеет — удаляется отдельно.
/// dry-run по умолчанию: план без изменений.
/// </summary>
public interface IResultTransferService
{
    /// <summary>
    /// Перенести результаты source → target. apply=false — dry-run (только отчёт).
    /// Бросает <see cref="System.ArgumentException"/> при source==target или ненайденном соревновании.
    /// </summary>
    Task<ResultTransferReport> MoveResultsAsync(
        int sourceCompetitionId, int targetCompetitionId, bool apply, CancellationToken ct = default);
}
