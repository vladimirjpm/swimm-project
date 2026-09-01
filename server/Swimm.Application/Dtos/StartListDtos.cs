using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

// Имена полей — snake_case, как у всей публичной выдачи (RecordDto, ClubPublicDtos).
// Атрибуты обязательны: кэширующий шов CachedJson сериализует ДЕФОЛТНЫМИ настройками,
// мимо политики именования MVC, и без них наружу поехал бы PascalCase.

/// <summary>
/// Один предстоящий заплыв пловца — строка стартового протокола для витрины
/// (docs/plans/start-list-plan.md §4).
/// </summary>
/// <param name="OrgCompId">
/// compID соревнования этого заплыва. Нужен, чтобы построить ссылку на стартовый протокол,
/// когда строка приходит ВНЕ контекста одного соревнования: «ближайшие старты» избранных
/// смешивают несколько разных стартов, и по одной строке иначе не понять, куда вести.
/// </param>
/// <param name="SeedTime">
/// Посевное время как напечатано («01:42.72»); null — «NT», пловец эту дистанцию ещё не плыл.
/// </param>
/// <param name="Quality">
/// ⚠ Всегда <c>«seed»</c> — и это НЕ формальность. Посевное время это личный рекорд пловца
/// С ДРУГОГО старта, по которому его посеяли; показать его рядом с результатами как время
/// этого заплыва — ровно тот класс ошибки, ради которого написан инвариант И11
/// (docs/data-integrity.md). До стартовых протоколов классов качества было два — протокол
/// и справочник рекордов; это третий.
/// </param>
/// <param name="HeatStartAt">
/// Момент старта заплыва (UTC). ⚠ Приблизительный: программа в бассейне регулярно отстаёт
/// на 20–40 минут, поэтому витрина пишет «≈» и опирается на номер заплыва.
/// null — время заплыву ещё не назначили.
/// </param>
/// <param name="Status">entered | swum | no-show — см. <c>CompetitionEntryStatus</c>.</param>
public sealed record StartListSwimDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("org_comp_id")] int OrgCompId,
    [property: JsonPropertyName("comp_name")] string CompName,
    [property: JsonPropertyName("org_discipline_id")] int OrgDisciplineId,
    [property: JsonPropertyName("event_number")] int? EventNumber,
    [property: JsonPropertyName("distance")] string Distance,
    [property: JsonPropertyName("style_name")] string StyleName,
    [property: JsonPropertyName("gender")] string Gender,
    [property: JsonPropertyName("event_category")] string? EventCategory,
    [property: JsonPropertyName("age_band")] string? AgeBand,
    [property: JsonPropertyName("is_relay")] bool IsRelay,
    [property: JsonPropertyName("heat")] int Heat,
    [property: JsonPropertyName("lane")] int Lane,
    /// <summary>
    /// Календарный день заплыва. Отдаётся отдельно от <c>HeatStartAt</c>: время заплыву
    /// могут не назначить вовсе, а «в какой день плывёт» — главный вопрос поиска по
    /// соревнованию, собранному из нескольких дней (окружные протоколы).
    /// </summary>
    [property: JsonPropertyName("comp_date")] DateTime CompDate,
    [property: JsonPropertyName("heat_start_at")] DateTime? HeatStartAt,
    [property: JsonPropertyName("round")] string? Round,
    [property: JsonPropertyName("seed_time")] string? SeedTime,
    [property: JsonPropertyName("quality")] string Quality,
    [property: JsonPropertyName("swimmer_id")] int SwimmerId,
    [property: JsonPropertyName("swimmer_name")] string SwimmerName,
    [property: JsonPropertyName("birth_year")] int? BirthYear,
    [property: JsonPropertyName("club_id")] int ClubId,
    [property: JsonPropertyName("club_name")] string ClubName,
    [property: JsonPropertyName("result_id")] long? ResultId,
    [property: JsonPropertyName("status")] string Status);

/// <summary>
/// Предстоящее соревнование для общего списка `/competitions` (решение В9 от 2026-08-27).
///
/// Источник — САМИ заявки, а не «Входящие» автозабора: <c>Sys_DiscoveredCompetitions</c>
/// приватна (нет гранта swimm_ro), и публичный read-путь её не видит. Заодно это ровно
/// правильный список: сюда попадают только те старты, для которых нам есть что показать.
/// </summary>
/// <param name="OrgCompId">
/// compID на isr.org.il — идентичность предстоящего старта. Своего <c>Competitions.Id</c>
/// у него ещё нет и до импорта протокола не будет.
/// </param>
/// <param name="Days">Дней в программе (у многодневки больше одного).</param>
public sealed record UpcomingCompetitionDto(
    [property: JsonPropertyName("org_comp_id")] int OrgCompId,
    [property: JsonPropertyName("comp_name")] string CompName,
    [property: JsonPropertyName("date_start")] DateTime DateStart,
    [property: JsonPropertyName("date_end")] DateTime DateEnd,
    [property: JsonPropertyName("days")] int Days,
    [property: JsonPropertyName("entries")] int Entries,
    [property: JsonPropertyName("swimmers")] int Swimmers,
    [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt);

/// <summary>Заплыв программы: строка ленты «всё соревнование по времени» (зум 1).</summary>
/// <param name="Entries">Сколько человек (у эстафет — ног) заявлено.</param>
/// <param name="Heats">Сколько заплывов внутри дисциплины.</param>
public sealed record StartListEventDto(
    [property: JsonPropertyName("org_discipline_id")] int OrgDisciplineId,
    [property: JsonPropertyName("event_number")] int? EventNumber,
    [property: JsonPropertyName("distance")] string Distance,
    [property: JsonPropertyName("style_name")] string StyleName,
    [property: JsonPropertyName("gender")] string Gender,
    [property: JsonPropertyName("event_category")] string? EventCategory,
    [property: JsonPropertyName("age_band")] string? AgeBand,
    [property: JsonPropertyName("is_relay")] bool IsRelay,
    [property: JsonPropertyName("start_at")] DateTime? StartAt,
    [property: JsonPropertyName("entries")] int Entries,
    [property: JsonPropertyName("heats")] int Heats);

/// <summary>Один день программы. У однодневного старта он единственный.</summary>
/// <param name="WarmUpAt">
/// Начало разминки в этот день (UTC) — из <c>CompetitionMeetInfos</c>/<c>CompetitionWarmUps</c>,
/// вводится руками в админке (шаг Т1). Из него витрина считает «приезжать к» (минус 30 минут).
/// null — не введено, и это обычное состояние: блок ARRIVE BY тогда не рисуется.
/// </param>
public sealed record StartListDayDto(
    [property: JsonPropertyName("date")] DateTime Date,
    [property: JsonPropertyName("events")] IReadOnlyList<StartListEventDto> Events,
    [property: JsonPropertyName("warm_up_at")] DateTime? WarmUpAt = null);

/// <summary>
/// Программа соревнования целиком (зум 1).
/// </summary>
/// <param name="UpdatedAt">
/// Когда стартовый протокол последний раз подтверждён забором. Витрина обязана это показать:
/// посев меняется до последнего дня, а механизма дожать изменение до уже открытой страницы
/// в проекте нет (§5 плана).
/// </param>
/// <param name="IsChampionship">
/// Чемпионат ли это — одно из трёх условий блока ARRIVE BY (шаг Т1). Значение действующее:
/// ручная правка администратора сильнее того, что определил забор по регламенту.
/// </param>
public sealed record StartListProgrammeDto(
    [property: JsonPropertyName("org_comp_id")] int OrgCompId,
    [property: JsonPropertyName("comp_name")] string CompName,
    [property: JsonPropertyName("days")] IReadOnlyList<StartListDayDto> Days,
    [property: JsonPropertyName("entries")] int Entries,
    [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt,
    [property: JsonPropertyName("is_championship")] bool IsChampionship = false);

/// <summary>Один заплыв с дорожками (зум 2): «с кем плывёт мой».</summary>
public sealed record StartListHeatDto(
    [property: JsonPropertyName("heat")] int Heat,
    [property: JsonPropertyName("start_at")] DateTime? StartAt,
    [property: JsonPropertyName("round")] string? Round,
    [property: JsonPropertyName("lanes")] IReadOnlyList<StartListSwimDto> Lanes);

/// <summary>Дисциплина с разбивкой по заплывам (зум 2).</summary>
public sealed record StartListEventHeatsDto(
    [property: JsonPropertyName("org_comp_id")] int OrgCompId,
    [property: JsonPropertyName("comp_name")] string CompName,
    [property: JsonPropertyName("event")] StartListEventDto Event,
    [property: JsonPropertyName("heats")] IReadOnlyList<StartListHeatDto> Heats,
    [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt);

/// <summary>
/// Клуб на соревновании — строка секции «follow a whole club» пикера (шаг Т2).
/// </summary>
/// <param name="ClubName">
/// Ивритское имя по умолчанию, английское — фоллбек: то же правило, что у имён пловцов
/// (решение Влада 28.08.2026). Название клуба — данные, а не строка интерфейса.
/// </param>
/// <param name="Swimmers">Сколько РАЗНЫХ пловцов клуба заявлено.</param>
/// <param name="Entries">
/// Сколько заплывов клуба в программе. Ноги эстафеты — ОДИН заплыв, а не четыре: у команды
/// четыре строки с одной парой заплыв+дорожка, и считать их порознь значит завышать клуб
/// с эстафетами (та же склейка, что <c>mergeRelayLanes</c> на витрине).
/// </param>
public sealed record StartListClubDto(
    [property: JsonPropertyName("club_id")] int ClubId,
    [property: JsonPropertyName("club_name")] string ClubName,
    [property: JsonPropertyName("swimmers")] int Swimmers,
    [property: JsonPropertyName("entries")] int Entries);

/// <summary>
/// Найденный пловец — строка выдачи поиска по имени внутри соревнования.
/// </summary>
/// <param name="Swims">Сколько заплывов заявлено (по всем источникам сразу).</param>
/// <param name="Days">Даты дней, в которые он плывёт: главный ответ поиска.</param>
public sealed record StartListSwimmerHitDto(
    [property: JsonPropertyName("swimmer_id")] int SwimmerId,
    [property: JsonPropertyName("swimmer_name")] string SwimmerName,
    [property: JsonPropertyName("birth_year")] int? BirthYear,
    [property: JsonPropertyName("club_name")] string ClubName,
    [property: JsonPropertyName("swims")] int Swims,
    [property: JsonPropertyName("days")] IReadOnlyList<DateTime> Days,
    [property: JsonPropertyName("first_start_at")] DateTime? FirstStartAt);

/// <summary>
/// Карточка пловца на соревновании (зум 3) — главный экран для родителя.
/// </summary>
/// <param name="FirstStartAt">
/// Первый старт дня: из него витрина считает «приезжать к» (минус разминка). Считать это
/// на клиенте нельзя — он не знает, какие заплывы принадлежат этому пловцу вне выдачи.
/// </param>
public sealed record StartListSwimmerDto(
    [property: JsonPropertyName("org_comp_id")] int OrgCompId,
    [property: JsonPropertyName("comp_name")] string CompName,
    [property: JsonPropertyName("swimmer_id")] int SwimmerId,
    [property: JsonPropertyName("swimmer_name")] string SwimmerName,
    [property: JsonPropertyName("birth_year")] int BirthYear,
    [property: JsonPropertyName("club_name")] string ClubName,
    [property: JsonPropertyName("first_start_at")] DateTime? FirstStartAt,
    [property: JsonPropertyName("swims")] IReadOnlyList<StartListSwimDto> Swims,
    [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt);
