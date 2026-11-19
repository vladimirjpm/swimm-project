using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт справочника «место проведения → длина бассейна» (П4, docs/plans/prod-auto-pull-plan.md §4.4).
/// Им пользуются превью «Затянуть» (одиночное и пакетное) и results-auto (П5).
///
/// Порядок приоритета у потребителей: регламент → справочник → как было; ручной выбор человека
/// сильнее всего. Справочник сам НИКОГДА не угадывает — см. <see cref="VenuePoolDirectory"/>.
/// </summary>
public interface IVenuePoolService
{
    /// <summary>Длина по одному месту.</summary>
    Task<VenuePoolLookup> LookupAsync(string? venue, CancellationToken ct = default);

    /// <summary>
    /// Весь справочник одним запросом — для прохода по многим строкам (results-auto). Снимок:
    /// правки на странице админки после загрузки он не видит.
    /// </summary>
    Task<VenuePoolDirectory> LoadAsync(CancellationToken ct = default);
}

/// <summary>Порт админского CRUD справочника бассейнов (/Admin/VenuePools).</summary>
public interface IVenuePoolAdminRepository
{
    Task<IReadOnlyList<VenuePoolRowDto>> GetAllAsync(CancellationToken ct = default);

    Task<VenuePoolRowDto?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Места «Входящих» (плавание), которых справочник не знает, — свежие сверху.</summary>
    Task<IReadOnlyList<UnknownVenueDto>> GetUnknownVenuesAsync(CancellationToken ct = default);

    /// <summary>Создать; <paramref name="confirmedBy"/> — кто подтвердил (имя админа).</summary>
    Task<VenuePoolSaveResult> CreateAsync(VenuePoolInputDto input, string confirmedBy, CancellationToken ct = default);

    /// <summary>Обновить: каждое сохранение — новое подтверждение (кто и когда).</summary>
    Task<VenuePoolSaveResult> UpdateAsync(int id, VenuePoolInputDto input, string confirmedBy, CancellationToken ct = default);

    Task<VenuePoolSaveResult> DeleteAsync(int id, CancellationToken ct = default);
}
