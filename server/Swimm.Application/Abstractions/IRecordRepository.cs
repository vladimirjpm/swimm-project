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
    /// <param name="withHolderDetails">
    /// Досыпать год рождения и возраст держателя (отладочная опция ShowAgeRecordsDetails).
    /// Отдельный параметр, а не чтение настройки внутри: репозиторий кэширует ответ, и
    /// подробности обязаны попадать в СВОЙ ключ кэша, иначе они «залипнут» после выключения.
    /// </param>
    Task<IReadOnlyList<RecordDto>> GetRecordsAsync(
        string region, string? category = null, bool withHolderDetails = false);

    /// <summary>
    /// Нормативы. kind: regular/masters; null — все.
    /// country: alpha-3 код системы нормативов (RUS/ISR/…); null — без фильтра (легаси).
    /// Задан — отдаёт строки с этой страной плюс универсальные (Country == "").
    /// </summary>
    Task<IReadOnlyList<NormativeStandardDto>> GetStandardsAsync(string? kind = null, string? country = null);
}
