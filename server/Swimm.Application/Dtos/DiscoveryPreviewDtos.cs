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
/// <param name="CollapsedProtocol">Р119: файл одной датой покрывает уже затянутые дни этого compID с
/// другими датами — импорт создал бы дубль; «Импортировать» заблокирована, пачка — ручная проверка.</param>
public sealed record DiscoveryPreviewResult(
    Guid PreviewId,
    ParsedCompetition? Parsed,
    IReadOnlyList<string> Languages,
    int? ExistingCompetitionId,
    IReadOnlyList<ExistingCompetitionMatch> ExistingCompetitions,
    ImportRecordPreviewDto? RecordPreview,
    OfficialClubStandingProbe? ClubStanding,
    CompetitionFlagSuggestion? Flags = null,
    string? Error = null,
    Validation.CollapsedProtocolFinding? CollapsedProtocol = null)
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
/// <param name="PoolType">Предложенная длина бассейна («25m»/«50m»): регламент → справочник бассейнов
/// по месту (П4); null — решает человек (см. <c>PoolTypeChooser</c>).</param>
/// <param name="RegulationUrl">Регламент, по которому предложены медали/чемпионат.</param>
/// <param name="Reasons">Обоснования по флагам: ключ — имя флага, значение — фраза для админа.</param>
/// <param name="CategoryKeys">
/// Категории, подобранные ПО НАЗВАНИЮ соревнования (правило Влада: есть в названии слово из
/// /Admin/Categories — применяем; возраста шире названных — добавляем «Age 8-99»).
/// </param>
/// <param name="PoolTypeSource">Откуда длина: <c>regulation</c> | <c>venue</c> (справочник) | null.</param>
/// <param name="VenuePool">Ответ справочника бассейнов по месту строки «Входящих» (null — не спрашивали).</param>
public sealed record CompetitionFlagSuggestion(
    bool IsAward,
    bool IsChampionship,
    bool IsMasters,
    bool ClubPointsDisabled,
    string? PoolType,
    string? RegulationUrl,
    IReadOnlyDictionary<string, string> Reasons,
    IReadOnlyList<string> CategoryKeys,
    string? PoolTypeSource = null,
    VenuePoolLookup? VenuePool = null);

/// <summary>Отложенный разбор в кэше — то, из чего потом собирается импорт.</summary>
/// <param name="Records">
/// Побитые рекорды этого файла. Лежат здесь, чтобы ленивая проверка по loglig
/// (<c>IPreviewRecordCheckService</c>) не пересчитывала детектор заново — она работает по
/// previewId, а не по файлу.
/// </param>
/// <param name="CollapsedProtocol">
/// Р119: находка «свёрнутый протокол» этого превью. Галочку обхода сервер принимает, только
/// если блок у этого превью был: согласие дают на показанный админу блок, а не вслепую.
/// </param>
/// <param name="SiblingLogligId">
/// Р124: разбор протокола соседа-дня (не привязанного к записи федерации номера loglig). Такой разбор
/// импортирует только «Затянуть этот день»: обычный импорт строки входящих его отвергает.
/// </param>
public sealed record DiscoveryPreviewEntry(
    ParsedCompetition Parsed, string FileName, int DiscoveredId, OfficialClubStandingProbe? ClubStanding,
    ImportRecordPreviewDto? Records = null,
    Validation.CollapsedProtocolFinding? CollapsedProtocol = null,
    int? SiblingLogligId = null);

/// <summary>Скачанный протокол одной строки входящих. <c>Pdf</c> = null — см. <c>Error</c>.</summary>
public sealed record DiscoveryProtocolPdf(byte[]? Pdf, string FileName, string? Error);
