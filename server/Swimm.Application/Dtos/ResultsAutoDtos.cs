namespace Swimm.Application.Dtos;

/// <summary>
/// Что results-auto сделала со строкой «Входящих» за тик (П5) — и итог прогона, и подробности
/// журнала (<c>Sys_JobRunLog.Details</c>), и то, что легло в поля строки.
/// </summary>
/// <param name="ClassifierVerdict">Вердикт <c>BulkPullClassifier</c>; null — до разбора не дошло (гейт без сети).</param>
/// <param name="ResultCount">Строк в разборе.</param>
/// <param name="DaysInFile">Дней (соревнований) в файле.</param>
/// <param name="Events">Событий протокола по <c>CompetitionProgress</c>.</param>
/// <param name="PendingEvents">Из них ещё не проплыто.</param>
public sealed record ResultsAutoRowOutcome(
    int DiscoveredId,
    int OrgCompId,
    string Name,
    DateTime DateEnd,
    string? Venue,
    string Verdict,
    IReadOnlyList<string> Reasons,
    string? PoolType,
    string? PoolTypeSource,
    string? ClassifierVerdict,
    int ResultCount,
    int DaysInFile,
    int Events,
    int PendingEvents,
    long ElapsedMs);

/// <summary>Переопределения тика (CLI). Пусто — как у расписания.</summary>
/// <param name="Mode">Режим вместо настройки <c>ResultsAutoMode</c>.</param>
/// <param name="DiscoveredId">Проверить эту строку, минуя окно и шаг повторной проверки.</param>
public sealed record ResultsAutoRunOptions(string? Mode = null, int? DiscoveredId = null);

/// <summary>Итог одного тика results-auto.</summary>
/// <param name="Mode">Режим, с которым тик шёл (<c>ResultsAutoModes</c>).</param>
/// <param name="InWindow">Строк в окне задачи.</param>
/// <param name="Due">Из них пора проверять.</param>
/// <param name="Row">Проверенная строка; null — проверять было некого (или режим off).</param>
/// <param name="ImportNotImplemented">Режим import запрошен, но импорт — этап П6: тик отработал как report.</param>
public sealed record ResultsAutoTickReport(
    string Mode,
    int InWindow,
    int Due,
    ResultsAutoRowOutcome? Row,
    bool ImportNotImplemented = false);
