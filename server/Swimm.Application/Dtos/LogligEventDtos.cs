namespace Swimm.Application.Dtos;

/// <summary>
/// Одна строка результата из ПОСОБЫТИЙНОГО источника loglig. В отличие от PDF-экспорта здесь
/// известен <paramref name="Round"/> — раунд зачёта, и есть официальные очки, посчитанные
/// самим организатором (И13, docs/data-integrity.md §10).
/// </summary>
/// <param name="Round">timed-final (גמר ישיר) / final (גמר) / prelim (מוקדמות).</param>
/// <param name="Category">Категория секции: «בנות 14», «כללי» у общего финала.</param>
/// <param name="FullName">Имя ОДНОЙ строкой, как печатает источник: «имя фамилия».</param>
/// <param name="FailNote">Статус вместо времени (DQ/NS/DNF) либо пометка рядом с ним (NMin).</param>
/// <param name="PersonalPoints">Личные очки организатора (5/3/2/1 за медали).</param>
/// <param name="ClubPoints">Клубные очки организатора — эталон для сверки нашего движка правил.</param>
public sealed record LogligResultRowDto(
    int? Position,
    string Round,
    string Category,
    string FullName,
    int? BirthYear,
    string Club,
    int Heat,
    int Lane,
    string? Time,
    string? FailNote,
    int InternationalPoints,
    int? PersonalPoints,
    int? ClubPoints);

/// <summary>Событие loglig целиком: шапка дисциплины и строки всех её секций.</summary>
public sealed record LogligEventResultsDto(
    string CompetitionName,
    string Date,
    string StyleName,
    string Distance,
    string Gender,
    string AgeBand,
    bool IsRelay,
    IReadOnlyList<LogligResultRowDto> Rows);
