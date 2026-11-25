using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Р124: «Затянуть этот день» в карточке /Admin/Competitions — день старта лежит на loglig
/// отдельным соревнованием-соседом (compID 488 → 9817, 6611 → 11590; находит его «Проверить, чего
/// не хватает», Р123). Второго импорта здесь нет: разбор — <see cref="IDiscoveryPreviewService"/>
/// с номером соседа, запись — очередь импорта (<see cref="IImportJobQueue"/>), сбор в событие —
/// <see cref="ICompetitionDayJoinService"/> или <c>ImportEventOptions.EventId</c>.
/// Сеть — только по клику; в пачки и results-auto этот путь не входит.
/// </summary>
public interface ISiblingDayPullService
{
    /// <summary>
    /// Превью дня: заново вердикт Р123 (кандидат ровно один и это <paramref name="logligId"/>),
    /// разбор протокола соседа, проверки даты и пересечения, значения по умолчанию от лежащего дня.
    /// </summary>
    Task<SiblingDayPreview> PreviewAsync(int discoveryId, int logligId, CancellationToken ct = default);

    /// <summary>
    /// «Импортировать»: сервер перепроверяет всё (кандидат, дата, дня нет в базе, пересечение,
    /// нет параллельного импорта этого старта) и ставит импорт в очередь; аудит
    /// <c>competition.pull-sibling-day</c>. Отказ — <see cref="SiblingDayEnqueue.Refusal"/>.
    /// </summary>
    Task<SiblingDayEnqueue> ImportAsync(int discoveryId, SiblingDayImportRequest request, string actor,
        CancellationToken ct = default);

    /// <summary>
    /// После импорта (фон, петля импорта): найти новый день, собрать в событие (одиночный
    /// лежащий день — <see cref="ICompetitionDayJoinService"/> с общим именем), записать аудит.
    /// </summary>
    Task<SiblingDayCompletion> CompleteAsync(SiblingDayTarget target, ImportResult result, CancellationToken ct = default);
}

/// <summary>
/// Что фон делает после импорта дня-соседа — едет в <see cref="ImportEventOptions.SiblingDay"/>.
/// <paramref name="EventId"/> — событие лежащего дня (импорт сам кладёт день в него); null —
/// лежащий день одиночный, после импорта дни собираются в событие <paramref name="NewEventName"/>.
/// </summary>
public sealed record SiblingDayTarget(
    int DiscoveryId,
    int OrgCompId,
    int LogligId,
    int ExistingCompetitionId,
    int? EventId,
    string? NewEventName,
    string Date,
    string Actor);

/// <summary>Превью: <paramref name="Refusal"/> — почему нельзя; иначе разбор и контекст дня.</summary>
public sealed record SiblingDayPreview(string? Refusal, DiscoveryPreviewResult? Preview, SiblingDayContext? Context)
{
    public static SiblingDayPreview Refused(string why) => new(why, null, null);
}

/// <summary>
/// Контекст дня для превью: кандидат, лежащий день (откуда значения по умолчанию), событие,
/// бассейн (<paramref name="PoolConflict"/> — у лежащего дня и у регламента разные длины:
/// не подставлено, выбор обязателен). <paramref name="SummaryTemplate"/> — строка над «Импортировать» с метками
/// <c>{pool}</c> и <c>{name}</c> (<see cref="MissingDayTexts.ImportSummaryTemplate"/>), их подставляет страница.
/// </summary>
public sealed record SiblingDayContext(
    int LogligId,
    string Title,
    string Date,
    int ExistingCompetitionId,
    string ExistingName,
    string ExistingDate,
    string? ExistingPool,
    int? EventId,
    string? EventName,
    string SuggestedEventName,
    string? RegulationPool,
    bool PoolConflict,
    int? PointRuleClubsId,
    int? PointRuleSwimmersId,
    int OverlapRows,
    string SummaryTemplate,
    string DefaultsNote,
    string? PoolConflictNote);

/// <summary>Тело «Импортировать» дня-соседа: то, что видел и поправил человек в превью.</summary>
public sealed record SiblingDayImportRequest(
    Guid PreviewId,
    int LogligId,
    string[]? CategoryKeys,
    string? EventName,
    string? PoolType,
    bool IsAward,
    bool IsChampionship,
    bool IsMasters,
    bool ClubPointsDisabled,
    int? PointRuleClubsId,
    IReadOnlyList<ImportSuspectFlag>? SuspectFlags = null);

public sealed record SiblingDayEnqueue(string? Refusal, Guid? JobId = null, int? EventId = null, string? EventName = null)
{
    public static SiblingDayEnqueue Refused(string why) => new(why);
}

/// <summary>Итог фона: новый день, событие, собрано ли; <paramref name="Message"/> — в журнал импорта.</summary>
public sealed record SiblingDayCompletion(int? CompetitionId, int? EventId, bool Joined, string Message);
