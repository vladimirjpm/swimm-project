namespace Swimm.Application.Dtos;

/// <summary>Насколько серьёзна находка. Порядок важен: сортировка и счётчики идут по нему.</summary>
public enum DataCheckSeverity { Info = 0, Warning = 1, Error = 2 }

/// <summary>
/// Виды точечных исправлений, доступных прямо из списка находок. Реестр остаётся читающим:
/// он лишь показывает кнопку, а чинит отдельный эндпоинт со своим аудитом.
/// </summary>
public static class DataCheckFixKinds
{
    /// <summary>Проставить пол пловцу (и его строкам без пола) — находка `results.no-gender`.</summary>
    public const string SwimmerGender = "swimmer-gender";

    /// <summary>
    /// Выровнять пол пловца — находка `results.gender-vs-card`. В отличие от
    /// <see cref="SwimmerGender"/> перезаписывает пол и в НЕПУСТЫХ личных строках: смысл
    /// находки как раз в том, что копии разошлись, и человек говорит, какая верна.
    /// </summary>
    public const string SwimmerGenderAlign = "swimmer-gender-align";

    /// <summary>
    /// Пара дедупа: «Принять как есть» на такой находке означает «это не дубли, а тёзки»
    /// и пишет пару в `Sys_DedupIgnoredPairs` — тот же список, что заводит ✕ на
    /// /Admin/Swimmers. Иначе механизма было бы два: находка принята, а в списке дублей
    /// пара продолжает висеть.
    /// </summary>
    public const string DedupIgnore = "dedup-ignore";

    /// <summary>
    /// Привязать правило клубных очков — находка `competitions.no-club-point-rule`.
    /// Правится селектом прямо в находке: заходить в карточку соревнования ради одного
    /// выбора незачем, а находок бывает десяток за раз.
    /// </summary>
    public const string CompetitionClubRule = "competition-club-rule";
}

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
/// <param name="Link">Куда идти ЧИНИТЬ — страница админки.</param>
/// <param name="PublicLink">
/// Куда идти СМОТРЕТЬ — публичная страница сайта, глазами на живые данные. Только
/// относительный путь по контракту <c>client/src/utils/routes.ts</c> («/competitions/{id}»,
/// «/swimmers/{id}»): базу подставляет страница при отрисовке, иначе dev-адрес осел бы в БД
/// и уехал в прод.
/// </param>
/// <param name="SubjectName">
/// Имя субъекта отдельным полем — чтобы его можно было скопировать одним кликом. В Message
/// оно вплавлено в текст, и выделять мышью из строки неудобно.
/// </param>
/// <param name="FixKind">Код точечного исправления из списка находок (напр. <c>swimmer-gender</c>).</param>
/// <param name="FixEntityId">Id сущности, к которой применяется исправление.</param>
public sealed record DataCheckItem(
    string EntityType, int? EntityId, string Message, string? Details = null, string? Link = null,
    string? PublicLink = null, string? SubjectName = null,
    string? FixKind = null, int? FixEntityId = null);

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
/// <param name="SubjectGender">
/// ТЕКУЩИЙ пол субъекта — читается на выдаче, а не хранится в находке: находка обновляется
/// только прогоном, и сохранённое значение врало бы сразу после исправления. Нужен, чтобы
/// человек видел, что он уже проставил, а что нет.
/// </param>
/// <param name="SubjectLogligId">Текущая привязка к loglig — по той же причине живая.</param>
public sealed record DataCheckFindingDto(
    int Id, string CheckId, DataCheckSeverity Severity, string EntityType, int? EntityId,
    string Message, string? Details, string? Link,
    DateTime FirstSeenAt, DateTime LastSeenAt, string? Resolution, string? Note,
    string? PublicLink = null, string? SubjectName = null,
    string? FixKind = null, int? FixEntityId = null,
    string? SubjectGender = null, int? SubjectLogligId = null,
    /// <summary>ТЕКУЩЕЕ правило клубных очков соревнования — живое, по той же причине,
    /// что пол и loglig: находка обновляется только прогоном и врала бы сразу после правки.</summary>
    int? SubjectClubRuleId = null,
    /// <summary>
    /// compID соревнования на isr.org.il — для ссылки на первоисточник (comp.asp?compID=).
    /// Живой по той же причине: штамп проставляется переимпортом и бэкфиллом Discovery,
    /// а находка обновляется только прогоном — сохранённый в находке оставался бы пустым до следующего.
    /// null — соревнование не сопоставлено с сайтом (или находка не про соревнование).
    /// </summary>
    int? SubjectOrgCompId = null);

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
/// <param name="Accepted">
/// Находок, принятых как есть («ошибка федерации, не чиним»). Вычитается из <paramref name="Total"/>:
/// иначе дашборд считал бы работой то, по чему решение уже принято, и расходился бы с
/// /Admin/Health, где принятые в «открытых» не значатся.
/// </param>
public sealed record DataCheckStateDto(
    string CheckId, DataCheckSeverity Severity, int Total, int Shown, bool Failed,
    int LastRunId, DateTime LastRunAt, int Accepted = 0)
{
    /// <summary>Сколько ещё требует разбора. Не отрицательное: принятых не может быть больше найденного.</summary>
    public int Open => Math.Max(0, Total - Accepted);
}
