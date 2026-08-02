using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Ретро-сверка уже загруженных протоколов (docs/data-integrity.md, фаза Д1).
/// Штатная сверка срабатывает в момент импорта, а данные, залитые раньше, никто не проверял:
/// парсер с тех пор чинился не раз. Аудит скачивает протокол заново, парсит текущим парсером
/// и сравнивает с БД.
///
/// ⚠ Диагноз, а не лечение: результаты НЕ меняются, пишется только журнал сверки
/// (<c>Sys_ImportReconciliation</c> с пометкой аудита). Чинить — переимпортом, точечно.
/// </summary>
public interface IImportAuditService
{
    /// <summary>
    /// Свериться по одной discovery-записи (её <c>Id</c>, не OrgCompId). Сеть: один-два
    /// запроса к loglig.
    /// </summary>
    Task<ImportAuditReport> AuditDiscoveredAsync(int discoveredId, CancellationToken ct = default);

    /// <summary>
    /// Свериться по всем импортированным записям, у которых известен источник (<c>LogligId</c>).
    /// <paramref name="limit"/> — ограничить число записей (первый прогон удобно делать на одной).
    /// </summary>
    Task<IReadOnlyList<ImportAuditReport>> AuditAllAsync(int? limit = null, CancellationToken ct = default);
}
