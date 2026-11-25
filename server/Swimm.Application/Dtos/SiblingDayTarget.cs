namespace Swimm.Application.Dtos;

/// <summary>
/// Что фон делает после импорта дня-соседа — едет в <see cref="ImportEventOptions.SiblingDay"/> (Р124, «Затянуть этот день»).
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
