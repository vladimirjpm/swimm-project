using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Проверка достоверности результатов соревнования («Проверить качество» в
/// /Admin/Competitions) и ручные пометки.
///
/// Ищет ошибки САМОГО источника — то, что не лечится парсером: протокол напечатан так,
/// как напечатан. Помеченный результат остаётся в протоколе, но выпадает из детекции
/// рекордов.
/// </summary>
public interface ISuspectResultService
{
    /// <summary>
    /// Прогоняет проверки по событию (все дни) или по одному дню-соревнованию и
    /// ПЕРЕЗАПИСЫВАЕТ автоматические пометки. Ручные (<c>manual</c>) не трогает —
    /// они переживают и переимпорт, и повторный прогон.
    /// </summary>
    Task<SuspectScanResultDto> ScanAsync(int? eventId, int? competitionId, CancellationToken ct = default);

    /// <summary>Что помечено сейчас — для показа на странице соревнования.</summary>
    Task<IReadOnlyList<SuspectRowDto>> GetFlaggedAsync(int? eventId, int? competitionId, CancellationToken ct = default);

    /// <summary>
    /// Ручная пометка/снятие одной строки. <paramref name="note"/> — пояснение человека;
    /// снятие (<paramref name="flagged"/> = false) убирает и автоматическую пометку тоже.
    /// </summary>
    Task<bool> SetManualAsync(long resultId, bool flagged, string? note, CancellationToken ct = default);
}
