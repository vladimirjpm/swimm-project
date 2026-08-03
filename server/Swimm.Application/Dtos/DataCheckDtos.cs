namespace Swimm.Application.Dtos;

/// <summary>Насколько серьёзна находка. Порядок важен: сортировка и счётчики идут по нему.</summary>
public enum DataCheckSeverity { Info = 0, Warning = 1, Error = 2 }

/// <summary>Чем закончилась находка. null в БД = ещё открыта.</summary>
public static class DataCheckResolutions
{
    /// <summary>Исчезла при следующем прогоне — данные починены.</summary>
    public const string Fixed = "fixed";

    /// <summary>Принята как есть: неустранимо (ошибка источника, особенность данных).</summary>
    public const string Accepted = "accepted";
}

/// <summary>
/// Одна находка проверки. <paramref name="EntityId"/> вместе с <paramref name="EntityType"/>
/// и Id проверки образуют ключ, по которому находка узнаётся между прогонами.
/// </summary>
public sealed record DataCheckItem(
    string EntityType, int? EntityId, string Message, string? Details = null, string? Link = null);

/// <summary>Результат одной проверки: сколько всего и что именно (список капнут).</summary>
public sealed record DataCheckOutcome(int Total, IReadOnlyList<DataCheckItem> Items)
{
    public static DataCheckOutcome Empty { get; } = new(0, []);
}

/// <summary>Прогон реестра.</summary>
public sealed record DataCheckRunDto(
    int Id, DateTime StartedAt, DateTime? FinishedAt, string Trigger,
    int ErrorCount, int WarningCount, int InfoCount, int FixedCount);

/// <summary>Находка в том виде, в каком её видит человек.</summary>
public sealed record DataCheckFindingDto(
    int Id, string CheckId, DataCheckSeverity Severity, string EntityType, int? EntityId,
    string Message, string? Details, string? Link,
    DateTime FirstSeenAt, DateTime LastSeenAt, string? Resolution, string? Note);

/// <summary>Находки одной проверки + её описание (для страницы /Admin/Health).</summary>
/// <param name="Total">
/// Сколько проверка нашла на последнем прогоне — ПОЛНОЕ число. Список находок капнут
/// (50 на проверку), поэтому <c>Total</c> может быть больше, чем <c>Findings.Count</c>;
/// null — проверка ещё ни разу не гонялась.
/// </param>
public sealed record DataCheckGroupDto(
    string CheckId, string Title, string Description, DataCheckSeverity Severity,
    int OpenCount, int AcceptedCount, IReadOnlyList<DataCheckFindingDto> Findings,
    int? Total = null, DateTime? LastRunAt = null, bool Failed = false);

/// <summary>
/// Итог последнего прогона одной проверки — то, чем дашборд заменяет собственные счётчики.
/// Полное число находок, не обрезанное списком.
/// </summary>
public sealed record DataCheckStateDto(
    string CheckId, DataCheckSeverity Severity, int Total, int Shown, bool Failed,
    int LastRunId, DateTime LastRunAt);
