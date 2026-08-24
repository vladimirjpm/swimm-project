namespace Swimm.Application.Dtos;

/// <summary>
/// Итог «затягивания» одной строки входящих: протокол скачан, разобран и отложен в кэш до
/// импорта. Одно и то же для одиночной кнопки «Затянуть» и для пакетного забора — поэтому
/// живёт в Application, а не в разметке ответа контроллера.
/// </summary>
/// <param name="PreviewId">Ключ отложенного разбора; по нему идёт импорт.</param>
/// <param name="Parsed">Разобранный протокол (для сводки; сам JSON уезжает в импорт из кэша).</param>
/// <param name="Languages">Языки скачанных PDF: he | en | he,en.</param>
/// <param name="ExistingCompetitionId">Соревнование уже есть в БД — импорт потребует решения о перезаписи.</param>
/// <param name="ExistingCompetitions">Совпадения по каждому дню файла.</param>
/// <param name="RecordPreview">Сколько рекордов побьёт файл (диагноз кривого разбора).</param>
/// <param name="ClubStanding">Официальный клубный зачёт: есть ли, по какой шкале, есть ли правило.</param>
/// <param name="Flags">Флаги соревнования, предложенные по источникам (см. <see cref="CompetitionFlagSuggestion"/>).</param>
/// <param name="Error">Разбор не удался; остальные поля тогда пусты.</param>
public sealed record DiscoveryPreviewResult(
    Guid PreviewId,
    ParsedCompetition? Parsed,
    IReadOnlyList<string> Languages,
    int? ExistingCompetitionId,
    IReadOnlyList<ExistingCompetitionMatch> ExistingCompetitions,
    ImportRecordPreviewDto? RecordPreview,
    OfficialClubStandingProbe? ClubStanding,
    CompetitionFlagSuggestion? Flags = null,
    string? Error = null)
{
    public static DiscoveryPreviewResult Failed(string error) =>
        new(Guid.Empty, null, [], null, [], null, null, null, error);
}

/// <summary>
/// Что превью предлагает проставить соревнованию — вместо того, чтобы человек вспоминал об
/// этом ПОСЛЕ импорта, в панели строки (и не вспоминал).
///
/// Это предложение, а не решение: галочки в превью изменяемы, и уезжает в импорт именно то,
/// что видел человек. Рядом с каждым флагом — <c>*Reason</c>, откуда он взялся: молча
/// проставленная галочка по чужому документу доверия не заслуживает.
/// </summary>
/// <param name="IsAward">Вручают медали — по регламенту (מדליות).</param>
/// <param name="IsChampionship">Чемпионат Израиля — по названию и/или регламенту.</param>
/// <param name="IsMasters">Мастерс — по разобранному файлу.</param>
/// <param name="ClubPointsDisabled">Клубный зачёт не ведётся — loglig показал «зачёта нет».</param>
/// <param name="PoolType">Длина бассейна, распознанная парсером («25m»/«50m»).</param>
/// <param name="RegulationUrl">Регламент, по которому предложены медали/чемпионат.</param>
/// <param name="Reasons">Обоснования по флагам: ключ — имя флага, значение — фраза для админа.</param>
public sealed record CompetitionFlagSuggestion(
    bool IsAward,
    bool IsChampionship,
    bool IsMasters,
    bool ClubPointsDisabled,
    string? PoolType,
    string? RegulationUrl,
    IReadOnlyDictionary<string, string> Reasons);

/// <summary>Отложенный разбор в кэше — то, из чего потом собирается импорт.</summary>
/// <param name="Records">
/// Побитые рекорды этого файла. Лежат здесь, чтобы ленивая проверка по loglig
/// (<c>IPreviewRecordCheckService</c>) не пересчитывала детектор заново — она работает по
/// previewId, а не по файлу.
/// </param>
public sealed record DiscoveryPreviewEntry(
    ParsedCompetition Parsed, string FileName, int DiscoveredId, OfficialClubStandingProbe? ClubStanding,
    ImportRecordPreviewDto? Records = null);

/// <summary>Скачанный протокол одной строки входящих. <c>Pdf</c> = null — см. <c>Error</c>.</summary>
public sealed record DiscoveryProtocolPdf(byte[]? Pdf, string FileName, string? Error);
