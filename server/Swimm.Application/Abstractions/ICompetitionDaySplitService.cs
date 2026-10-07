namespace Swimm.Application.Abstractions;

/// <summary>
/// Деление схлопнутой многодневки по дням БЕЗ переимпорта (docs/plans/multiday-split-plan.md, М2).
///
/// Строки переносятся UPDATE-ом: их Id не меняется, поэтому медиа, реакции, ручные пометки,
/// этапы эстафет и промежуточные едут вместе со строкой. Переимпорт с правильными датами так не
/// умеет: он завёл бы второй день отдельным соревнованием, а строки первого дня в старом — задвоил
/// или удалил бы вместе с ручными правками.
/// </summary>
public interface ICompetitionDaySplitService
{
    /// <param name="competitionId">Однодневное соревнование (без события), которое на деле многодневное.</param>
    /// <param name="logligId">Пособытийный источник дат; null — из discovery по OrgCompId.</param>
    /// <param name="apply">false — только план (по умолчанию в CLI).</param>
    Task<CompetitionDaySplitReport> SplitAsync(
        int competitionId, int? logligId, bool apply, CancellationToken ct = default);
}

/// <summary>Итог деления.</summary>
/// <param name="Refusal">Почему делить нельзя; null — можно (или поделено).</param>
/// <param name="Unresolved">Дисциплина без дня (или с неоднозначным) → строк.</param>
public sealed record CompetitionDaySplitReport(
    int CompetitionId,
    string Name,
    int? LogligId,
    IReadOnlyList<CompetitionDaySplitDay> Days,
    IReadOnlyDictionary<string, int> Unresolved,
    string? Refusal,
    bool Applied,
    int? EventId)
{
    public static CompetitionDaySplitReport Refused(int competitionId, string name, int? logligId, string why) =>
        new(competitionId, name, logligId, [], new Dictionary<string, int>(), why, false, null);
}

/// <param name="CompetitionId">Соревнование дня; null у нового дня в dry-run.</param>
/// <param name="Existing">День, которым становится само делимое соревнование.</param>
/// <param name="Media">Медиа пользователей на строках этого дня (едут вместе со строками).</param>
public sealed record CompetitionDaySplitDay(
    DateOnly Date, int DayNumber, int? CompetitionId, bool Existing, int Rows, int Media);
