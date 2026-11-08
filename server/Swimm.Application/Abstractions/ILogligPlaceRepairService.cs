using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Сверка личных строк соревнования с пособытийным источником loglig и ремонт мест (И-62).
/// Пара к <see cref="ILogligRelayBandService"/>: тот правит полосы эстафет, этот — места
/// личных строк, которые PDF-экспорт печатает сквозными через оба пола (секция «כללי»).
/// Строки не пересоздаются: время, заплыв, дорожка и пловец уже правильные.
/// </summary>
public interface ILogligPlaceRepairService
{
    /// <param name="discoveredId">Запись Sys_DiscoveredCompetitions — из неё берётся LogligId.</param>
    /// <param name="apply">false — только сверка и план; true — записать места и пересчитать дни.</param>
    Task<LogligPlaceReport> RepairAsync(int discoveredId, bool apply, CancellationToken ct = default);
}

/// <summary>Итог прогона.</summary>
/// <param name="Events">Личных событий разобрано.</param>
/// <param name="SourceRows">Личных строк у источника.</param>
/// <param name="DbRows">Личных строк у нас (по всем дням события).</param>
/// <param name="Applied">Сколько строк реально обновлено (0 у dry-run).</param>
/// <param name="UnreadableEvents">События, которые loglig не отдал (HTTP-ошибка).</param>
public sealed record LogligPlaceReport(
    string CompetitionName,
    int Events,
    int SourceRows,
    int DbRows,
    int Applied,
    PlacePlan Plan,
    IReadOnlyList<int> UnreadableEvents);
