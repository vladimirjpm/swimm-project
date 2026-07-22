using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// «Входящие» автозабора: синхронизация Sys_DiscoveredCompetitions со списком сайта,
/// выдача для админки, смена статуса. Скачивание PDF и импорт — через
/// <see cref="ICompetitionDiscoveryProvider"/> + существующий PDF-пайплайн.
/// </summary>
public interface ICompetitionDiscoveryService
{
    /// <summary>Обновить «входящие» со списка сайта (завершённые + предстоящие).</summary>
    Task<DiscoverySyncResult> SyncAsync(CancellationToken ct = default);

    /// <summary>Все обнаруженные, новые сверху; статус imported дополняется матчем по дате+имени.</summary>
    Task<IReadOnlyList<DiscoveredCompetitionDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Догрузить детали (venue, logligId) для записи; сохраняет LastError при сбое.</summary>
    Task<DiscoveredCompetitionDto?> RefreshDetailsAsync(int id, CancellationToken ct = default);

    /// <summary>Сменить статус (new | imported | ignored). false — записи нет или статус неизвестен.</summary>
    Task<bool> SetStatusAsync(int id, string status, CancellationToken ct = default);

    /// <summary>Бэкфилл связи Discovery → Competition: проставить OrgCompId сматченному
    /// (по имени+дате) соревнованию. Для строк, импортированных до штампа OrgCompId.</summary>
    Task<RelinkResult> LinkImportedAsync(int id, CancellationToken ct = default);

    /// <summary>Разовый CLI-бэкфилл всех Discovery-строк: проставляет OrgCompId сматченным
    /// (по имени+дате) соревнованиям, импортированным до появления штампа OrgCompId. dry-run
    /// при apply=false (БД не меняется, Action=WouldLink); apply=true пишет одним SaveChanges.</summary>
    Task<IReadOnlyList<DiscoveryBackfillRow>> BackfillImportedOrgCompIdsAsync(bool apply, CancellationToken ct = default);

    /// <summary>Дописать языки успешно загруженных PDF (объединение с уже сохранёнными,
    /// канонический порядок "he,en"). false — записи нет.</summary>
    Task<bool> AddLanguagesAsync(int id, IEnumerable<string> languages, CancellationToken ct = default);

    /// <summary>Записать/очистить LastError записи (диагностика «затянуть»/«синхр. языки» в админке).</summary>
    Task<bool> SetLastErrorAsync(int id, string? error, CancellationToken ct = default);
}
