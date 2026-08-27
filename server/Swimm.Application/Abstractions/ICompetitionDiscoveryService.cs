using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// «Входящие» автозабора: синхронизация Sys_DiscoveredCompetitions со списком сайта,
/// выдача для админки, смена статуса. Скачивание PDF и импорт — через
/// <see cref="ICompetitionDiscoveryProvider"/> + существующий PDF-пайплайн.
/// </summary>
public interface ICompetitionDiscoveryService
{
    /// <summary>
    /// Обновить «входящие» со списка сайта (завершённые + предстоящие).
    /// year — сезон сайта (cYear), null = текущий; прошлые сезоны тянутся тем же путём.
    /// </summary>
    Task<DiscoverySyncResult> SyncAsync(int? year = null, CancellationToken ct = default);

    /// <summary>Все обнаруженные, новые сверху; статус imported дополняется матчем по дате+имени.</summary>
    Task<IReadOnlyList<DiscoveredCompetitionDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Догрузить детали (venue, logligId) для записи; сохраняет LastError при сбое.</summary>
    Task<DiscoveredCompetitionDto?> RefreshDetailsAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Дочитать детальные страницы будущих стартов, у которых ещё нет loglig-id.
    /// Возвращает (проверено, добыто).
    /// </summary>
    Task<(int Checked, int Resolved)> RefreshUpcomingDetailsAsync(
        int daysAhead, CancellationToken ct = default);

    /// <summary>OrgCompId (compID isr.org.il) записи «входящих» по её Id. null — записи нет.
    /// Нужен контроллерам, которым для вызова другого сервиса (стартовый протокол) нужна
    /// идентичность по OrgCompId, а маршрут адресует записи по Id, как соседние методы.</summary>
    Task<int?> GetOrgCompIdAsync(int id, CancellationToken ct = default);

    /// <summary>Сменить статус (new | imported | ignored). false — записи нет или статус неизвестен.</summary>
    Task<bool> SetStatusAsync(int id, string status, CancellationToken ct = default);

    /// <summary>Ручная правка вида спорта строки (Disciplines). false — нет записи или
    /// значение неизвестно.</summary>
    Task<bool> SetDisciplineAsync(int id, string discipline, CancellationToken ct = default);

    /// <summary>Разовый CLI-бэкфилл всех Discovery-строк: проставляет OrgCompId сматченным
    /// (по имени+дате) соревнованиям, импортированным до появления штампа OrgCompId. dry-run
    /// при apply=false (БД не меняется, Action=WouldLink); apply=true пишет одним SaveChanges.</summary>
    Task<IReadOnlyList<DiscoveryBackfillRow>> BackfillImportedOrgCompIdsAsync(bool apply, CancellationToken ct = default);

    /// <summary>Дописать языки успешно загруженных PDF (объединение с уже сохранёнными,
    /// канонический порядок "he,en"). false — записи нет.</summary>
    Task<bool> AddLanguagesAsync(int id, IEnumerable<string> languages, CancellationToken ct = default);

    /// <summary>Записать/очистить LastError записи (диагностика «затянуть»/«синхр. языки» в админке).</summary>
    Task<bool> SetLastErrorAsync(int id, string? error, CancellationToken ct = default);

    /// <summary>
    /// Пометить/снять «у соревнования нет протокола» (PDF пуст). Отдельно от ошибки: ошибку
    /// имеет смысл повторить, а пустой источник — повод больше не пытаться.
    /// </summary>
    Task<bool> SetEmptySourceAsync(int id, bool empty, string by, CancellationToken ct = default);
}
