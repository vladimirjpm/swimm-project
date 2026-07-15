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
}
