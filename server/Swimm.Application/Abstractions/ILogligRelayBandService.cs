using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Ремонт зачётных полос эстафет по пособытийному источнику loglig
/// (docs/data-integrity.md §10). Пара к <see cref="ILogligEventPullService"/>: тот
/// перетягивает ЛИЧНЫЕ заплывы целиком, а этот трогает только те поля эстафетных строк,
/// которых нет в PDF, — пол полосы, возрастную полосу и место внутри неё.
///
/// Раздельно, потому что риск разный: личные строки переимпортируются (ключ upsert меняется
/// вместе с <c>Round</c>), а эстафетные ОБНОВЛЯЮТСЯ на месте — их состав, ноги и
/// <c>RelayId</c> уже правильные, и пересоздавать их значило бы оборвать привязки.
/// </summary>
public interface ILogligRelayBandService
{
    /// <summary>
    /// Скачать эстафетные события соревнования, сопоставить их строки с нашими и вернуть план.
    /// </summary>
    /// <param name="discoveredId">Запись Sys_DiscoveredCompetitions — из неё берётся LogligId.</param>
    /// <param name="apply">
    /// false — только план (по умолчанию). true — записать изменения и пересчитать
    /// зачёт соревнования. Неприменимый план (<see cref="RelayBandPlan.CanApply"/> = false)
    /// не записывается никогда.
    /// </param>
    Task<LogligRelayBandReport> RepairAsync(int discoveredId, bool apply, CancellationToken ct = default);
}

/// <summary>Итог прогона: что нашлось у источника, что поменяется и что уже записано.</summary>
/// <param name="CompetitionName">Название соревнования по данным loglig.</param>
/// <param name="RelayEvents">Сколько эстафетных событий разобрано.</param>
/// <param name="SourceRows">Строк эстафет у источника.</param>
/// <param name="DbRows">Строк эстафет у нас (по всем дням соревнования).</param>
/// <param name="OfficialPoints">Сумма официальных клубных очков эстафет.</param>
/// <param name="PointsBefore">Наши очки эстафет ДО ремонта, по привязанному правилу.</param>
/// <param name="PointsAfter">Наши очки эстафет ПОСЛЕ ремонта, по тому же правилу.</param>
/// <param name="Applied">Сколько строк реально обновлено (0 у dry-run).</param>
public sealed record LogligRelayBandReport(
    string CompetitionName,
    int RelayEvents,
    int SourceRows,
    int DbRows,
    int OfficialPoints,
    int PointsBefore,
    int PointsAfter,
    int Applied,
    RelayBandPlan Plan);
