using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Проверка свежести справочника рекордов (docs/plans/records-freshness-plan.md, U2–U3):
/// сходить в источник, построить dry-run дифф и записать строку журнала
/// (<c>Sys_RecordSourceChecks</c>). Применение — по-прежнему руками, но через этот же сервис,
/// чтобы журнал знал, какая проверка применена.
///
/// Расписания нет (решение §7-2): кнопка и CLI. Сервис написан так, чтобы на Azure его
/// просто позвал <c>BackgroundService</c> — второй реализации не будет.
/// </summary>
public interface IRecordSourceCheckService
{
    /// <summary>
    /// Проверить один источник. <paramref name="request"/> с файлами — ручная загрузка в
    /// админке, без них — провайдер качает сам. Сбой источника не бросается, а пишется
    /// строкой <c>failed</c> и возвращается с текстом.
    /// </summary>
    Task<RecordSourceCheckResultDto> CheckAsync(
        string source, RecordSourceRequest? request = null, CancellationToken ct = default);

    /// <summary>Все источники в порядке <c>RecordSources.Order</c>; упавший не останавливает остальных.</summary>
    Task<IReadOnlyList<RecordSourceCheckResultDto>> CheckAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Apply диффа с отметкой в журнале. Дифф устаревшей проверки (после неё источник уже
    /// проверяли снова) не применяется: применять надо то, что источник говорит СЕЙЧАС.
    /// Дифф без строки журнала (прогон по странам) проходит как раньше.
    /// </summary>
    Task<RecordDiffApplyResult> ApplyAsync(RecordDiffApplyRequest request, CancellationToken ct = default);

    /// <summary>
    /// Записать в журнал проверку, которую сделал НЕ провайдер, а собственный прогон —
    /// сейчас это прогон по странам (<c>RecordSources.WorldRecordsCountries</c>). Дифф он
    /// строит сам, и качать источник второй раз ради журнала незачем: сюда приходит уже
    /// готовый результат.
    /// </summary>
    Task LogRunAsync(
        string source, RecordDiffResult? diff, string? error, CancellationToken ct = default);

    /// <summary>Свежесть по каждому источнику — для админки (дашборд, /Admin/Import) и витрины.</summary>
    Task<IReadOnlyList<RecordSourceFreshnessDto>> GetFreshnessAsync(CancellationToken ct = default);
}
