namespace Swimm.Application.Abstractions;

/// <summary>Команда с невозможным этапом: что было записано и почему стираем.</summary>
public sealed record RelayLegCleanupRow(
    int RelayId, int CompetitionId, string Distance, string Style, string? Time, string Reason,
    IReadOnlyList<string?> OldSplits);

/// <summary>Итог чистки. <c>Applied</c> = false — сухой прогон, база не тронута.</summary>
public sealed record RelayLegCleanupReport(
    bool Applied, int RelaysChecked, IReadOnlyList<RelayLegCleanupRow> Rows, int LegsCleared);

/// <summary>
/// Разовая чистка этапов эстафет, записанных ДО сторожа невозможного этапа
/// (<see cref="Mapping.RelayLegFloor"/>, решение Влада 10.10.2026, docs/relays.md
/// «Невозможный этап»). Правило то же, что у доклейки: невозможен хоть один этап — у команды
/// этапов нет совсем. Запись — одной транзакцией, аудит <c>relay.splits-clear</c> на команду
/// со старыми значениями (откат — по аудиту).
/// </summary>
public interface IRelayLegCleanupService
{
    /// <param name="apply">false — сухой прогон.</param>
    /// <param name="competitionIds">Только эстафеты этих соревнований (null — вся база). Так его зовёт
    /// импорт: сборка мастерских эстафет кладёт этапы ног в обход доклейки, и перезалив
    /// #1482 10.10.2026 вернул только что обнулённый этап 00:00.69.</param>
    Task<RelayLegCleanupReport> RunAsync(
        bool apply, IReadOnlyCollection<int>? competitionIds = null, CancellationToken ct = default);
}
