using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Состояние всех фоновых служб процесса (docs/admin-pages/jobs.md): страница /Admin/Jobs и блок
/// «Jobs» дашборда берут его отсюда, подсчёт сводки один — <c>JobsOverviewBuilder</c>.
/// Реализация живёт в API-слое: ей нужны флаги процесса (<c>HostingMode</c>) и пинг БД.
/// </summary>
public interface IBackgroundJobsStatus
{
    /// <summary>Полный отчёт для /Admin/Jobs: карточки с историей прогонов, сводка, последние импорты.</summary>
    Task<BackgroundJobsReport> GetReportAsync(CancellationToken ct = default);

    /// <summary>Только сводка — для дашборда: без истории прогонов и без журналов очередей.</summary>
    Task<JobsSummary> GetSummaryAsync(CancellationToken ct = default);
}
