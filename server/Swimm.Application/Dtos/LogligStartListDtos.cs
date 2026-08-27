namespace Swimm.Application.Dtos;

/// <summary>
/// Одна строка СТАРТОВОГО протокола: план заплыва, а не его результат
/// (docs/plans/start-list-plan.md).
/// </summary>
/// <param name="LogligId">
/// id пловца на loglig — имя в стартовом протоколе напечатано ссылкой на карточку.
/// Главный ключ матчинга: имена в этом источнике только на иврите, английского экспорта нет.
/// null — ссылки не было.
/// </param>
/// <param name="SeedTime">
/// Посевное время («זמן כניסה») — личный рекорд пловца С ДРУГОГО старта, по которому его
/// посеяли. Это НЕ время этого соревнования: показывать его как результат нельзя, у него
/// свой класс качества (И11, docs/data-integrity.md). null — в протоколе «NT».
/// </param>
/// <param name="HeatStartAt">
/// Время старта ЗАПЛЫВА, местное израильское, без даты и часового пояса
/// (<c>HH:mm</c>). Дату даёт <see cref="LogligDisciplineGridRowDto.StartAtLocal"/>.
/// </param>
public sealed record LogligStartListRowDto(
    int Heat,
    int Lane,
    int? LogligId,
    string FullName,
    int? BirthYear,
    string Club,
    string? SeedTime,
    string Round,
    string? HeatStartAt);

/// <summary>Стартовый протокол одного заплыва целиком.</summary>
public sealed record LogligStartListDto(
    string CompetitionName,
    string Date,
    string DisciplineRaw,
    string StyleName,
    string Distance,
    bool IsRelay,
    IReadOnlyList<LogligStartListRowDto> Rows);

/// <summary>
/// Строка сетки заплывов дня — программа соревнования.
/// </summary>
/// <param name="DisciplineId">id ЗАПЛЫВА на loglig (не соревнования); ключ идентичности заявки.</param>
/// <param name="StartAtLocal">
/// Дата и время старта заплыва, МЕСТНЫЕ израильские (<c>Kind=Unspecified</c>): источник
/// печатает их без часового пояса, перевод в UTC — забота вызывающего. Отсюда же берётся
/// день многодневки. null — время заплыву ещё не назначили, и это норма.
/// </param>
/// <param name="Registered">«סה"כ נרשמים» — записалось; шире стартового протокола.</param>
/// <param name="Participants">«סה"כ משתתפים» — участвует. Разница — неявки.</param>
public sealed record LogligDisciplineGridRowDto(
    int DisciplineId,
    int? EventNumber,
    string DisciplineRaw,
    string Category,
    string StyleName,
    string Distance,
    string Gender,
    string AgeBand,
    bool IsRelay,
    string? MinTime,
    DateTime? StartAtLocal,
    int Registered,
    int Participants);
