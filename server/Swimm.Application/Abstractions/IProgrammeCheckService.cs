using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Р123: «Проверить, чего не хватает» у недотянутой многодневки (Р115) в карточке /Admin/Competitions.
/// Берёт программу loglig (даты дисциплин), дни старта в базе и — если нужно — dry-run деления
/// <see cref="ICompetitionDaySplitService"/>; вердикт выносит <see cref="ProgrammeCheckRules"/>.
/// Сеть — только по вызову (клик в карточке), не при открытии страницы.
/// </summary>
public interface IProgrammeCheckService
{
    /// <param name="discoveryId">Строка входящих (DiscoveredCompetitions.Id) — у неё даты федерации и loglig-id.</param>
    Task<ProgrammeCheckVerdict> CheckAsync(int discoveryId, CancellationToken ct = default);

    /// <summary>
    /// «Разделить по дням»: заново та же проверка; только при вердикте «дни склеены» без помех и
    /// при том же плане, что видел админ (<paramref name="planFingerprint"/>, <see cref="ProgrammeCheckVerdict.PlanFingerprint"/>),
    /// — деление в режиме записи (как CLI <c>--split-days --apply</c>) и аудит <c>competition.split-days</c>.
    /// Иначе — отказ с причиной. После начала записи токен запроса не учитывается.
    /// </summary>
    Task<ProgrammeSplitOutcome> SplitAsync(int discoveryId, string? planFingerprint, CancellationToken ct = default);
}

/// <param name="Refusal">Почему не делили; null — поделено (<paramref name="Report"/>).</param>
/// <param name="RecalcError">Деление записано, но пересчёт дней после commit упал — его надо повторить.</param>
public sealed record ProgrammeSplitOutcome(string? Refusal, CompetitionDaySplitReport? Report, string? RecalcError = null)
{
    public static ProgrammeSplitOutcome Refused(string why) => new(why, null);
}
