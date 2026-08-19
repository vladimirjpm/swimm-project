namespace Swimm.Application.Abstractions;

/// <summary>
/// Затягивание результатов соревнования из ПОСОБЫТИЙНОГО источника loglig — там, где
/// PDF-экспорт беднее сайта: он склеивает утреннюю и вечернюю сессии чемпионата
/// «мокдамот и финал» в один список, теряя раунд (И13, docs/data-integrity.md §10).
///
/// Пока только РАЗВЕДКА (dry-run): служба скачивает события, разбирает их и отчитывается,
/// что получилось бы, ничего не записывая. Импорт — отдельное осознанное действие, как и
/// весь остальной ремонт данных.
/// </summary>
public interface ILogligEventPullService
{
    /// <summary>
    /// Пройти по всем событиям соревнования (по записи автозабора) и вернуть отчёт.
    /// </summary>
    /// <param name="discoveredId">Запись Sys_DiscoveredCompetitions — из неё берётся LogligId.</param>
    Task<LogligPullReport> DryRunAsync(int discoveredId, CancellationToken ct = default);
}

/// <summary>Итог разведки: что нашлось у источника и что мешает импортировать.</summary>
/// <param name="CompetitionName">Название соревнования по данным loglig.</param>
/// <param name="Events">Сколько событий у соревнования.</param>
/// <param name="IndividualRows">Строк личных заплывов.</param>
/// <param name="RelayEvents">Эстафетных событий (пока НЕ поддержаны — состав команд страница не печатает).</param>
/// <param name="RowsByRound">Строк по раундам: timed-final / final / prelim.</param>
/// <param name="UnresolvedNames">Имена, которые не удалось сопоставить с пловцами в БД.</param>
/// <param name="OfficialClubPoints">Официальные клубные очки по клубам (сумма колонки «ניקוד קבוצתי»).</param>
public sealed record LogligPullReport(
    string CompetitionName,
    int Events,
    int IndividualRows,
    int RelayEvents,
    IReadOnlyDictionary<string, int> RowsByRound,
    IReadOnlyList<string> UnresolvedNames,
    IReadOnlyDictionary<string, int> OfficialClubPoints);
