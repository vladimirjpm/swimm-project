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
    /// Пол с карточки loglig ждёт решения человека — находка `swimmers.gender-from-loglig-review`
    /// (Р126): автоматика не проставила, потому что есть серьёзные последствия или личность не
    /// подтверждена заплывами. Кнопка «Проставить» пишет пол с карточки осознанно; «Оставить как
    /// есть» — обычное «Принять».
    /// </summary>
    public const string SwimmerGenderLogligReview = "swimmer-gender-loglig";

    /// <summary>
    /// Пара дедупа: «Принять как есть» на такой находке означает «это не дубли, а тёзки»
    /// и пишет пару в `Sys_DedupIgnoredPairs` — тот же список, что заводит ✕ на
    /// /Admin/Swimmers. Иначе механизма было бы два: находка принята, а в списке дублей
    /// пара продолжает висеть.
    /// </summary>
    public const string DedupIgnore = "dedup-ignore";

    /// <summary>
    /// Не исправление, а составной ключ находки: «пловец (EntityId) в соревновании (FixEntityId)» —
    /// находка `swimmers.two-loglig-players` (Р122). Без него находка держалась бы на одном пловце,
    /// и «Принять как есть» для одного старта (дубль игрока на стороне loglig) навсегда глушило бы
    /// ту же карточку в других — настоящее слияние там не всплыло бы. Кнопки у вида нет.
    /// </summary>
    public const string SwimmerInCompetition = "swimmer-in-competition";

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

    /// <summary>
    /// Начало примечания находки, принятой САМОЙ проверкой (<see cref="DataCheckItem.AutoAccept"/>).
    /// По нему реестр отличает своё решение от решения человека: своё снимает, когда находка
    /// перестала быть «честной», человеческое — никогда.
    /// </summary>
    public const string AutoNotePrefix = "автоматически: ";
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
/// <param name="AutoAccept">
/// Не null — проверка сама уверена, что смотреть нечего, и принимает находку с этим примечанием
/// (решение Влада 25.11.2026: «Заявки без результата», где все неявки честные). Реестр ставит
/// «Принято» с примечанием <see cref="DataCheckResolutions.AutoNotePrefix"/> + текст; стал null —
/// своё решение снимает. Принятое человеком не трогает.
/// </param>
public sealed record DataCheckItem(
    string EntityType, int? EntityId, string Message, string? Details = null, string? Link = null,
    string? PublicLink = null, string? SubjectName = null,
    string? FixKind = null, int? FixEntityId = null, string? AutoAccept = null);

/// <summary>Результат одной проверки: сколько всего и что именно (список капнут).</summary>
/// <param name="Total">
/// Полное число находок — В ТЕХ ЖЕ ЕДИНИЦАХ, что <paramref name="Items"/>: одна единица = одна
/// находка. Проверка, которая группирует (неявки по соревнованию, а не по заявке), считает
/// группы, а не то, что внутри них: из <c>Total</c> вычитаются принятые находки
/// (<see cref="DataCheckOpenCount"/>), и разные единицы дают открытые из ничего — 26.11
/// дашборд показал 37 предупреждений «44 заявки − 7 принятых соревнований» при нуле в Health.
/// </param>
public sealed record DataCheckOutcome(int Total, IReadOnlyList<DataCheckItem> Items)
{
    public static DataCheckOutcome Empty { get; } = new(0, []);
}

/// <summary>
/// ЕДИНСТВЕННОЕ правило «сколько у проверки открыто». Им считают и /Admin/Health
/// (<see cref="DataCheckGroupDto.OpenCount"/>), и дашборд с итогом импорта
/// (<see cref="DataCheckStateDto.Open"/>) — раньше у них были две свои формулы, и числа разошлись.
/// </summary>
public static class DataCheckOpenCount
{
    /// <param name="total">Полное число с последнего прогона; null — проверка ещё не гонялась.</param>
    /// <param name="openListed">Открытых находок в записанном списке (он капнут на 50).</param>
    /// <param name="accepted">Принятых как есть — закрытое решение, а не работа.</param>
    /// <remarks>
    /// Полное минус принятые: список обрезан, и по нему одному большая проверка показала бы 50
    /// вместо тысяч. Но не меньше открытых в списке — то, что человек видит списком, число не
    /// может отрицать.
    /// </remarks>
    public static int Of(int? total, int openListed, int accepted) =>
        total is null ? openListed : Math.Max(openListed, total.Value - accepted);
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
/// <param name="OpenCount">Сколько открыто — по общему правилу <see cref="DataCheckOpenCount"/>, как у дашборда.</param>
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
/// <param name="OpenListed">Открытых находок в записанном списке — нужен общему правилу подсчёта.</param>
public sealed record DataCheckStateDto(
    string CheckId, DataCheckSeverity Severity, int Total, int Shown, bool Failed,
    int LastRunId, DateTime LastRunAt, int Accepted = 0, int OpenListed = 0)
{
    /// <summary>Сколько ещё требует разбора — по общему правилу, тем же, что у /Admin/Health.</summary>
    public int Open => DataCheckOpenCount.Of(Total, OpenListed, Accepted);
}
