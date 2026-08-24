namespace Swimm.Application.Dtos;

/// <summary>Карточка игрока loglig.com (Players/Details/{id}).</summary>
public sealed record LogligPlayerCard(
    string FullName,
    int? BirthYear,
    string? Gender,
    string? ClubName,
    IReadOnlyList<LogligResultRow> Results);

/// <summary>
/// Сырой ответ loglig про клубный зачёт соревнования: опубликован ли он и по какой шкале
/// считается. Шкала снята с заплывов; правило под неё подбирает уже <c>PointRuleScaleMatcher</c>.
/// </summary>
/// <param name="HasStanding">Таблица «דירוג מועדונים» непустая.</param>
/// <param name="Scale">Место → клубные очки (мода по нескольким индивидуальным заплывам).</param>
public sealed record LogligCompetitionStanding(
    bool HasStanding,
    IReadOnlyDictionary<int, int> Scale);

/// <summary>
/// Регламент соревнования («תקנון») — PDF, на который ссылается страница соревнования loglig
/// (<c>LeagueTable/ShowLeagueDoc/{DocId}</c>). Ссылка есть НЕ у всех соревнований.
/// </summary>
/// <param name="DocId">Идентификатор документа на loglig — он же строит URL внутри клиента.</param>
/// <param name="Url">Публичный адрес PDF — показываем админу как основание.</param>
/// <param name="FileName">Имя для анализатора (в сообщениях об ошибке чтения).</param>
/// <param name="Pdf">Содержимое файла; никуда не сохраняется, живёт только на время разбора.</param>
public sealed record LogligRegulationDoc(int DocId, string Url, string FileName, byte[] Pdf);

/// <summary>Строка таблицы личных рекордов (pld-pb-table) карточки игрока.</summary>
public sealed record LogligResultRow(
    string EventRaw,        // «100 חופשי» как на сайте
    string? Distance,       // «100», «4X50»
    string? StyleName,      // freestyle/... по маппингу; null если стиль не распознан
    bool IsRelay,
    int PoolLength,         // 25/50
    string TimeRaw,         // «01:32.68»
    int? TimeMillisecond,
    DateTime Date,          // из dd/MM/yyyy, DateTimeKind.Utc
    string CompetitionName);

/// <summary>
/// Участник соревнования, как его печатает страница заплыва loglig: имя ссылкой на карточку.
/// Отсюда берётся loglig-id ДО импорта — в том числе для пловца, которого в нашей базе ещё нет.
/// </summary>
/// <param name="LogligId">Id карточки из ссылки <c>/Players/Details/{id}</c>.</param>
/// <param name="FullName">Имя одной строкой, как на сайте («имя фамилия»).</param>
/// <param name="BirthYear">Год рождения из соседней колонки; null — не напечатан (эстафеты).</param>
public sealed record LogligParticipant(int LogligId, string FullName, int? BirthYear);

/// <summary>
/// Итог штамповки loglig-id по протоколу соревнования.
/// </summary>
/// <param name="Swimmers">Сколько пловцов соревнования рассмотрено.</param>
/// <param name="AlreadyLinked">Из них уже были привязаны — их не трогаем.</param>
/// <param name="Stamped">Скольким привязали id из протокола.</param>
/// <param name="NotFound">Не нашлись среди участников на loglig (имя разошлось / нет ссылки).</param>
/// <param name="Skipped">Пропущены с причиной: тёзки в базе, id занят другим пловцом.</param>
/// <param name="Message">Готовая строка для лога и админки.</param>
public sealed record LogligStampReport(
    int Swimmers,
    int AlreadyLinked,
    int Stamped,
    int NotFound,
    IReadOnlyList<string> Skipped,
    string Message);

/// <summary>Итог разового прохода штамповки по всем импортированным соревнованиям.</summary>
/// <param name="Competitions">Сколько соревнований обошли (у которых был долг).</param>
/// <param name="Stamped">Сколько пловцов привязали всего.</param>
/// <param name="NotFound">Сколько не нашлось в протоколах.</param>
/// <param name="Skipped">Сколько пропущено (тёзки, занятый id).</param>
/// <param name="Lines">Построчный отчёт для консоли: по строке на соревнование.</param>
public sealed record LogligStampBackfillReport(
    int Competitions,
    int Stamped,
    int NotFound,
    int Skipped,
    IReadOnlyList<string> Lines);
