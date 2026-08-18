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
