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

    /// <summary>
    /// Перетянуть соревнование из пособытийного источника: скачать события, сопоставить имена,
    /// собрать JSON и импортировать поверх существующего (upsert с удалением исчезнувших —
    /// у строк меняется ключ из-за <c>Round</c>). Эстафеты сохраняются: их источник не несёт.
    /// </summary>
    /// <returns>Отчёт разведки и итог импорта одной строкой.</returns>
    Task<(LogligPullReport Report, string ImportSummary)> ImportAsync(
        int discoveredId, CancellationToken ct = default);

    /// <summary>
    /// Разведка ЭСТАФЕТ по составам со страниц событий (Р107): сколько команд, сколько ног
    /// узнано (по id карточки loglig, иначе по имени в ростере), что не узнано.
    /// </summary>
    Task<LogligRelayPullReport> RelaysDryRunAsync(int discoveredId, CancellationToken ct = default);

    /// <summary>
    /// Заменить эстафеты соревнования эстафетами со страниц событий (Р107): upsert с удалением
    /// исчезнувших ТОЛЬКО среди эстафет, личные строки не трогаются (PreserveIndividuals).
    /// Отказывает, если хоть одна нога или команда не узнана — иначе часть эстафет пропала бы.
    /// </summary>
    Task<(LogligRelayPullReport Report, string ImportSummary)> ImportRelaysAsync(
        int discoveredId, CancellationToken ct = default);
}

/// <summary>Итог разведки эстафет со страниц событий.</summary>
/// <param name="Teams">Строк-команд у источника.</param>
/// <param name="TeamsWithoutLegs">Команд, у которых страница не напечатала состав.</param>
/// <param name="Legs">Ног всего.</param>
/// <param name="LegsByLogligId">Узнано по id карточки loglig.</param>
/// <param name="LegsByName">Узнано по имени (единственная пара в ростере соревнования).</param>
/// <param name="UnresolvedLegs">Не узнанные ноги: «имя (id) · клуб».</param>
/// <param name="OurRelays">Эстафетных строк у нас сейчас.</param>
public sealed record LogligRelayPullReport(
    string CompetitionName,
    int RelayEvents,
    int Teams,
    int TeamsWithoutLegs,
    int Legs,
    int LegsByLogligId,
    int LegsByName,
    IReadOnlyList<string> UnresolvedLegs,
    int OurRelays)
{
    /// <summary>Импорт возможен: все команды с составом и все ноги узнаны.</summary>
    public bool CanImport => Teams > 0 && TeamsWithoutLegs == 0 && UnresolvedLegs.Count == 0;
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
