using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>Строка отчёта починки EN-имён: что было, что решили и почему.</summary>
public sealed record EnglishNameRepairRow(
    int SwimmerId, string HeName, int BirthYear, int? LogligId,
    string OldLastEn, string OldFirstEn, string? Card, EnglishNameRepairDecision Decision);

/// <summary>Итог прогона. <c>Applied</c> = false — сухой прогон, база не тронута.</summary>
public sealed record EnglishNameRepairReport(
    bool Applied, IReadOnlyList<EnglishNameRepairRow> Rows, int Written, int Cleared, int Kept, int Skipped);

/// <summary>
/// Починка английских имён пловцов, которые не подходят к ивритским (10.10.2026,
/// docs/plans/en-names-handoff.md). Кандидаты — те же, что находит проверка
/// <c>swimmers.en-name-mismatch</c>; решение — <see cref="EnglishNameRepairPlanner"/> по
/// английской карточке loglig. Запись — одной транзакцией, только если поля не поменялись с
/// момента чтения (иначе строка пропускается), с аудитом <c>swimmer.en-name-fix</c> на каждую.
/// </summary>
public interface IEnglishNameRepairService
{
    /// <param name="apply">false — сухой прогон: карточки читаются, база не меняется.</param>
    /// <param name="onlySwimmerIds">Ограничить пловцами (null — все кандидаты).</param>
    Task<EnglishNameRepairReport> RepairAsync(
        bool apply, IReadOnlyCollection<int>? onlySwimmerIds = null, CancellationToken ct = default);
}
