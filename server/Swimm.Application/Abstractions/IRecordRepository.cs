using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Публичное чтение рекордов и нормативов (read-only путь, swimm_ro).
/// Кэш режется по региону/категории — добавление данных новой страны
/// не инвалидирует горячие выборки остальных.
/// </summary>
public interface IRecordRepository
{
    /// <summary>
    /// Рекорды региона. region: "world" | код континента (EU/AS) | ISO-код страны (ISR).
    /// category: open/age/junior/masters; null — все категории региона.
    /// </summary>
    Task<IReadOnlyList<RecordDto>> GetRecordsAsync(string region, string? category = null);

    /// <summary>Нормативы. kind: regular/masters; null — все.</summary>
    Task<IReadOnlyList<NormativeStandardDto>> GetStandardsAsync(string? kind = null);
}
