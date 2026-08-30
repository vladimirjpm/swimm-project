using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using System.Globalization;
using System.Text;

namespace Swimm.API.Controllers;

/// <summary>
/// Админ-API «входящих» автозабора isr.org.il (фаза 6): синхронизация списка, детали,
/// «затянуть» (PDF из loglig → превью существующего парсера → импорт через очередь).
/// </summary>
[ApiController]
[Route("api/admin/discovery")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class DiscoveryAdminController : ControllerBase
{
    private readonly ICompetitionDiscoveryService _discovery;
    private readonly IDiscoveryPreviewService _previews;
    private readonly IResultSourceProvider _sourceProvider;
    private readonly ISwimmerNameSyncService _nameSync;
    private readonly IImportJobQueue _jobs;
    private readonly IImportService _import;
    private readonly IMemoryCache _cache;
    private readonly IImportRecordPreviewService _recordPreview;
    private readonly IOfficialClubStandingService _clubStandings;
    private readonly IPointRulesAdminRepository _rules;
    private readonly IRegulationFetchService _regulationFetch;
    private readonly IBulkPullService _bulkPull;
    private readonly IPreviewRecordCheckService _recordCheck;
    private readonly ILogligStampService _logligStamp;
    private readonly IAdminAuditService _audit;
    private readonly IStartListPullService _startList;
    private readonly IMeetInfoAdminService _meetInfo;
    private readonly ILogger<DiscoveryAdminController> _logger;

    public DiscoveryAdminController(
        ICompetitionDiscoveryService discovery,
        IDiscoveryPreviewService previews,
        IResultSourceProvider sourceProvider,
        ISwimmerNameSyncService nameSync,
        IImportJobQueue jobs,
        IImportService import,
        IMemoryCache cache,
        IImportRecordPreviewService recordPreview,
        IOfficialClubStandingService clubStandings,
        IPointRulesAdminRepository rules,
        IRegulationFetchService regulationFetch,
        IBulkPullService bulkPull,
        IPreviewRecordCheckService recordCheck,
        ILogligStampService logligStamp,
        IAdminAuditService audit,
        IStartListPullService startList,
        IMeetInfoAdminService meetInfo,
        ILogger<DiscoveryAdminController> logger)
    {
        _discovery = discovery;
        _previews = previews;
        _sourceProvider = sourceProvider;
        _nameSync = nameSync;
        _jobs = jobs;
        _import = import;
        _cache = cache;
        _recordPreview = recordPreview;
        _clubStandings = clubStandings;
        _rules = rules;
        _regulationFetch = regulationFetch;
        _bulkPull = bulkPull;
        _recordCheck = recordCheck;
        _logligStamp = logligStamp;
        _audit = audit;
        _startList = startList;
        _meetInfo = meetInfo;
        _logger = logger;
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetList(CancellationToken ct)
        => Ok(await _discovery.GetAllAsync(ct));

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromQuery] int? year, CancellationToken ct = default)
    {
        // year — сезон isr.org.il (cYear); null = текущий. Ограничение — здравый диапазон,
        // чтобы случайный ?year=1 не гонял чужой прод впустую.
        if (year is not null and (< 2000 or > 2100))
            return BadRequest(new { error = "Сезон вне диапазона 2000–2100." });
        try
        {
            return Ok(await _discovery.SyncAsync(year, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            // B4: ошибка вёрстки/сети — явным текстом в админку, не тихий ноль.
            _logger.LogWarning(ex, "Discovery sync failed");
            return StatusCode(502, new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/details")]
    public async Task<IActionResult> RefreshDetails(int id, CancellationToken ct)
    {
        var dto = await _discovery.RefreshDetailsAsync(id, ct);
        return dto is null ? NotFound(new { error = "Запись не найдена" }) : Ok(dto);
    }

    /// <summary>
    /// Затянуть стартовый протокол (docs/plans/start-list-plan.md, шаг С5). id — как у соседей,
    /// Sys_DiscoveredCompetitions.Id; идентичность самого забора — OrgCompId. Статус empty
    /// (посев не сделан / нет loglig-id) — ожидаемое состояние источника, не ошибка.
    /// </summary>
    [HttpPost("{id:int}/start-list")]
    public async Task<IActionResult> PullStartList(int id, CancellationToken ct)
    {
        var orgCompId = await _discovery.GetOrgCompIdAsync(id, ct);
        if (orgCompId is null) return NotFound(new { error = "Запись не найдена" });

        var report = await _startList.PullAsync(orgCompId.Value, ct);
        return Ok(report);
    }

    /// <summary>
    /// Справка о старте (шаг Т1): чемпионат + разминка по дням. Читается редактором в
    /// модале «Разминка» на строке соревнования.
    /// </summary>
    [HttpGet("{id:int}/meet-info")]
    [IgnoreAntiforgeryToken] // GET, мутаций нет
    public async Task<IActionResult> GetMeetInfo(int id, CancellationToken ct)
    {
        var orgCompId = await _discovery.GetOrgCompIdAsync(id, ct);
        if (orgCompId is null) return NotFound(new { error = "Запись не найдена" });

        var info = await _meetInfo.GetAsync(orgCompId.Value, ct);
        return info is null ? NotFound(new { error = "Соревнование неизвестно" }) : Ok(info);
    }

    /// <summary>
    /// Сохранить справку. Пишутся ТОЛЬКО ручные поля: время разминки и переопределение
    /// флага «чемпионат». Сам флаг ставит забор по регламенту — сюда он не приходит,
    /// иначе следующий же перезабор затёр бы решение админа.
    /// </summary>
    [HttpPost("{id:int}/meet-info")]
    public async Task<IActionResult> SaveMeetInfo(
        int id, [FromBody] MeetInfoSaveRequest request, CancellationToken ct)
    {
        var orgCompId = await _discovery.GetOrgCompIdAsync(id, ct);
        if (orgCompId is null) return NotFound(new { error = "Запись не найдена" });

        var info = await _meetInfo.SaveAsync(orgCompId.Value, request, ct);
        return info is null ? NotFound(new { error = "Соревнование неизвестно" }) : Ok(info);
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] SetStatusRequest request, CancellationToken ct)
        => await _discovery.SetStatusAsync(id, request.Status, ct)
            ? Ok(new { ok = true })
            : BadRequest(new { error = "Запись не найдена или статус неизвестен" });

    /// <summary>Правка вида спорта строки: эвристика по названию иногда промахивается.</summary>
    [HttpPost("{id:int}/discipline")]
    public async Task<IActionResult> SetDiscipline(int id, [FromBody] SetDisciplineRequest request, CancellationToken ct)
        => await _discovery.SetDisciplineAsync(id, request.Discipline, ct)
            ? Ok(new { ok = true })
            : BadRequest(new { error = "Запись не найдена или дисциплина неизвестна" });

    /// <summary>Скачать PDF-протокол вручную (для существующего Import-флоу или глазами посмотреть).</summary>
    [HttpGet("{id:int}/pdf")]
    [IgnoreAntiforgeryToken] // GET-скачивание файла; мутаций нет
    public async Task<IActionResult> DownloadPdf(int id, [FromQuery] string language = "he", CancellationToken ct = default)
    {
        var p = await _previews.FetchProtocolAsync(id, language, refreshIfMissing: false, ct);
        if (p.Pdf is null) return BadRequest(new { error = p.Error });
        return File(p.Pdf, "application/pdf", p.FileName);
    }

    /// <summary>«Затянуть»: скачать оба PDF из loglig (HE + EN) и прогнать через парсер → превью.
    /// Вся работа — в <see cref="IDiscoveryPreviewService"/>: её же зовёт пакетный забор.</summary>
    [HttpPost("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, CancellationToken ct = default)
    {
        var p = await _previews.PreviewAsync(id, ct);
        if (p.Error != null) return BadRequest(new { error = p.Error });

        return Ok(new
        {
            previewId = p.PreviewId,
            format = p.Parsed!.Format,
            resultCount = p.Parsed.ResultCount,
            competitions = p.Parsed.Competitions,
            warnings = p.Parsed.Warnings,
            languages = p.Languages,
            existingCompetitionId = p.ExistingCompetitionId,
            existingCompetitions = p.ExistingCompetitions,
            recordPreview = p.RecordPreview,
            officialClubStanding = StandingResponse(p.ClubStanding, p.Parsed),
            // Флаги соревнования (медали, чемпионат, мастерс, «зачёт не ведётся», бассейн)
            // с обоснованиями. Предложение — галочки в превью изменяемы.
            flags = p.Flags
        });
    }

    public sealed record EmptySourceRequest(bool Empty);

    /// <summary>
    /// Пометить/снять вручную «у соревнования нет протокола». Нужно для случаев, когда PDF
    /// пуст не навсегда или наоборот — админ знает, что протокола не будет.
    /// </summary>
    [HttpPost("{id:int}/empty-source")]
    public async Task<IActionResult> SetEmptySource(int id, [FromBody] EmptySourceRequest request, CancellationToken ct = default)
    {
        var by = User.Identity?.Name ?? "admin";
        if (!await _discovery.SetEmptySourceAsync(id, request.Empty, by, ct))
            return NotFound(new { error = $"Строка входящих {id} не найдена" });

        return Ok(new { id, request.Empty });
    }

    /// <summary>
    /// Ленивая проверка подозрительных заплывов превью по карточкам loglig: то ли это время,
    /// что напечатано в протоколе. Отдельным запросом, а не внутри превью — карточки тянутся
    /// по одной на пловца, и разбор из-за них ждать не должен.
    /// </summary>
    [HttpPost("preview/{previewId:guid}/record-check")]
    public async Task<IActionResult> RecordCheck(Guid previewId, CancellationToken ct = default)
        => Ok(await _recordCheck.CheckAsync(previewId, ct));

    public sealed record StampLogligRequest(int OrgCompId);

    /// <summary>
    /// Проставить пловцам соревнования loglig-id из его же протокола. На импорте это делается
    /// само (настройка `LogligStampOnImport`); кнопка нужна для уже импортированных стартов —
    /// их сотня, а привязок в базе меньше сотни на 5.5 тысяч пловцов.
    /// </summary>
    [HttpPost("stamp-loglig")]
    public async Task<IActionResult> StampLoglig([FromBody] StampLogligRequest request, CancellationToken ct)
    {
        var report = await _logligStamp.StampFromProtocolAsync(request.OrgCompId, ct);

        if (report.Stamped > 0)
            await _audit.LogAsync("swimmer.stamp-loglig", "Competition", request.OrgCompId.ToString(),
                $"compID {request.OrgCompId}: {report.Message}",
                new { request.OrgCompId, report.Stamped, report.AlreadyLinked, report.NotFound, report.Skipped }, ct);

        return Ok(report);
    }

    // ── Пакетное затягивание (docs/plans/bulk-pull-plan.md) ───────────────────

    public sealed record BulkPullStartRequest(int[] Ids, bool IncludeChampionships = false);

    /// <summary>
    /// Затянуть пачкой всё, что видно в текущей выборке фильтров. Работа фоновая: ответ
    /// отдаёт batchId, дальше панель поллит состояние.
    /// </summary>
    [HttpPost("bulk-pull")]
    public async Task<IActionResult> BulkPull([FromBody] BulkPullStartRequest request, CancellationToken ct)
    {
        if (request.Ids is null || request.Ids.Length == 0)
            return BadRequest(new { error = "Список пуст — нечего затягивать." });

        var batch = await _bulkPull.StartAsync(request.Ids, request.IncludeChampionships, ct);

        await _audit.LogAsync("competition.bulk-pull", "DiscoveredCompetition", null,
            $"Пакетное затягивание: {batch.Total} строк"
            + (request.IncludeChampionships ? " (включая чемпионаты)" : "")
            + (batch.SkippedChampionships.Count > 0 ? $", исключено чемпионатов: {batch.SkippedChampionships.Count}" : ""),
            new { batch.BatchId, batch.Total, request.IncludeChampionships, batch.SkippedChampionships }, ct);

        return Accepted(batch);
    }

    /// <summary>Состояние пачки — поллинг панели.</summary>
    [HttpGet("bulk-pull/{batchId:guid}")]
    [IgnoreAntiforgeryToken] // GET-чтение статуса, мутаций нет
    public IActionResult BulkPullStatus(Guid batchId)
    {
        var batch = _bulkPull.GetStatus(batchId);
        return batch is null
            ? NotFound(new { error = "Пачка не найдена — возможно, приложение перезапускалось. Затяните заново." })
            : Ok(batch);
    }

    public sealed record BulkImportRequest(Guid BatchId, int[] Ids);

    /// <summary>
    /// Импортировать отмеченные строки пачки. Категория всем одна (results-8-99),
    /// перезапись и удаление лишнего не применяются никогда.
    /// </summary>
    [HttpPost("bulk-import")]
    public async Task<IActionResult> BulkImport([FromBody] BulkImportRequest request, CancellationToken ct)
    {
        if (request.Ids is null || request.Ids.Length == 0)
            return BadRequest(new { error = "Не отмечено ни одной строки." });

        var result = await _bulkPull.ImportAsync(request.BatchId, request.Ids, ct);

        // Основания решений (цитаты регламента) уезжают в аудит: в пачке галочки ставятся
        // автоматически, и проверить «почему» должно быть можно постфактум.
        var batch = _bulkPull.GetStatus(request.BatchId);
        var imported = batch?.Rows.Where(r => request.Ids.Contains(r.DiscoveredId)).Select(r => new
        {
            r.DiscoveredId, r.OrgCompId, r.Name, verdict = r.Verdict.ToString(),
            r.HasMedals, r.RegulationUrl, r.PointRuleClubsId, r.RegulationFindings
        }).ToList();

        await _audit.LogAsync("competition.bulk-import", "DiscoveredCompetition", null,
            $"Пакетный импорт: в очередь {result.Queued}"
            + (result.Skipped.Count > 0 ? $", пропущено {result.Skipped.Count}" : ""),
            new { request.BatchId, result.Queued, result.Skipped, rows = imported }, ct);

        return Ok(result);
    }

    public sealed record RegulationRequest(int? DiscoveredId, int? OrgCompId);

    /// <summary>
    /// Забрать регламент (תקנון) САМИ и разобрать — без файла от админа.
    ///
    /// Регламент лежит не на isr.org.il, а на loglig: на странице соревнования стоит ссылка
    /// «תקנון» → <c>ShowLeagueDoc/{docId}</c> (PDF). Поэтому вход — loglig-id из «входящих»;
    /// у соревнования, которого нет во «входящих» (PDF-импорт), взять его неоткуда — там
    /// остаётся загрузка файла руками.
    /// </summary>
    [HttpPost("regulation")]
    public async Task<IActionResult> Regulation([FromBody] RegulationRequest request, CancellationToken ct = default)
    {
        var rows = await _discovery.GetAllAsync(ct);
        var row = request.DiscoveredId is int did
            ? rows.FirstOrDefault(d => d.Id == did)
            : rows.FirstOrDefault(d => d.OrgCompId == request.OrgCompId);

        if (row is null)
            return NotFound(new { error = "Соревнования нет во «входящих» — регламент можно только приложить файлом." });

        if (row.LogligId is not int logligId)
            return BadRequest(new { error = "У строки ещё не загружены детали (нет loglig-id) — нажмите «Затянуть» или «Обновить»." });

        var fetched = await _regulationFetch.FetchAsync(logligId, ct);
        if (!fetched.Found)
            return BadRequest(new { error = fetched.Error, url = fetched.Url });

        var a = fetched.Analysis!;
        return Ok(new
        {
            url = fetched.Url,
            hasMedals = a.HasMedals,
            hasClubStanding = a.HasClubStanding,
            isChampionship = a.IsChampionship,
            findings = a.Findings
        });
    }

    /// <summary>«Синхронизировать языки»: скачать оба PDF, склеить пару и дозаполнить
    /// EN/HE-имена пловцов в БД по уже импортированным результатам (без переимпорта).</summary>
    [HttpPost("{id:int}/sync-languages")]
    public async Task<IActionResult> SyncLanguages(int id, CancellationToken ct = default)
    {
        // Ошибки пишем в LastError записи — тост в админке живёт секунды, строка таблицы — нет.
        _logger.LogInformation("Discovery sync-languages: старт (id={Id})", id);

        var he = await _previews.FetchProtocolAsync(id, "he", refreshIfMissing: true, ct);
        if (he.Pdf is null) return await SyncFailedAsync(id, $"HE-протокол недоступен: {he.Error}", ct);
        var en = await _previews.FetchProtocolAsync(id, "en", refreshIfMissing: false, ct);
        if (en.Pdf is null) return await SyncFailedAsync(id, $"EN-экспорт недоступен: {en.Error}", ct);
        var (pdfHe, fileNameHe) = (he.Pdf, he.FileName);
        var (pdfEn, fileNameEn) = (en.Pdf, en.FileName);

        ParsedCompetition parsed;
        try
        {
            using var ms = new MemoryStream(pdfHe);
            using var msEn = new MemoryStream(pdfEn);
            parsed = await _sourceProvider.ParseAsync(new ResultSourceRequest(
                ms, fileNameHe, "IsrOrg",
                IsAward: false, PoolType: null,
                SecondaryStream: msEn, SecondaryFileName: fileNameEn, ExtraFiles: null,
                Country: null, Language: "he"));
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("No competitions found in PDF (language=") && pdfHe.Length == pdfEn.Length)
        {
            // Обе культуры отдали один и тот же файл (размер совпадает до байта, отличаются
            // только метаданные) — отдельной второй версии на loglig не существует
            // (Maccabiah-кейс). Это не ошибка, синхронизировать нечего.
            _logger.LogInformation("Discovery sync-languages: протокол одноязычный (id={Id})", id);
            await _discovery.AddLanguagesAsync(id, ["he"], ct);
            await _discovery.SetLastErrorAsync(id, null, ct);
            return Ok(new
            {
                monolingual = true,
                message = "Второй языковой версии протокола на loglig нет (обе культуры отдают один файл) — синхронизировать нечего."
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No competitions found in PDF (language="))
        {
            // Файлы разные (EN-версия существует), но парсер не распознал формат одной из
            // сторон — например, EN-ветка не знает мастерс-заголовки. Честная ошибка.
            return await SyncFailedAsync(id,
                $"EN-версия на loglig есть (файлы разного размера), но парсер не распознал её формат: {FirstLine(ex.Message)}", ct);
        }
        catch (InvalidOperationException ex)
        {
            return await SyncFailedAsync(id, $"Склейка HE+EN пары не удалась: {ex.Message}", ct);
        }

        var summary = await _nameSync.SyncFromResultsJsonAsync(parsed.ResultsJson, ct);
        await _discovery.AddLanguagesAsync(id, ["he", "en"], ct);
        await _discovery.SetLastErrorAsync(id, null, ct);
        _logger.LogInformation(
            "Discovery sync-languages: готово (id={Id}) — в протоколе {Total}, EN дозаполнено {Filled}, канонизировано {Canonized}, полных {Complete}, не найдено {NotFound}",
            id, summary.SwimmersInProtocol, summary.EnNamesFilled, summary.Canonized,
            summary.AlreadyComplete, summary.NotFound);
        return Ok(summary);
    }

    /// <summary>Первая строка сообщения парсера — без многостраничного DEBUG LOG.</summary>
    private static string FirstLine(string message)
    {
        var idx = message.IndexOf('\n');
        return idx > 0 ? message[..idx].TrimEnd() : message;
    }

    private async Task<IActionResult> SyncFailedAsync(int id, string error, CancellationToken ct)
    {
        _logger.LogWarning("Discovery sync-languages: ошибка (id={Id}): {Error}", id, error);
        await _discovery.SetLastErrorAsync(id, $"Синхр. языки: {error}", ct);
        return BadRequest(new { error });
    }

    /// <summary>Импорт превью в очередь (аналог import-parsed) + пометка записи imported.</summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] DiscoveryImportRequest request, CancellationToken ct)
    {
        var entry = _previews.GetEntry(request.PreviewId);
        if (entry == null)
            return NotFound(new { error = $"Превью не найдено или истекло ({_previews.EntryLifetime.TotalMinutes:0} мин)" });

        _previews.RemoveEntry(request.PreviewId);

        // Правило клубных очков приезжает из превью: там оно подставлено по шкале
        // официального зачёта. Без него соревнование ушло бы на автоподбор по дате —
        // ровно так зимний чемпионат 2025 получил чужую шкалу (§10.3 плана).
        //
        // Опции собираем ВСЕГДА: кроме события и перезаписи в них теперь едут флаги
        // соревнования (медали, чемпионат, мастерс, «зачёт не ведётся», бассейн), а они
        // бывают заданы и без всего остального.
        var eventOptions = new ImportEventOptions(
            request.EventId, request.NewEventName, request.OverwriteExisting, request.DeleteMissing,
            request.PointRuleClubsId,
            IsAward: request.IsAward,
            IsChampionship: request.IsChampionship,
            IsMasters: request.IsMasters,
            ClubPointsDisabled: request.ClubPointsDisabled,
            PoolType: request.PoolType);

        // compID сайта — штампуется в Competition.OrgCompId для связи Discovery ↔ Competitions.
        var discoveredOrgCompId = (await _discovery.GetAllAsync(ct))
            .FirstOrDefault(d => d.Id == entry.DiscoveredId)?.OrgCompId;

        // Галочки «пометить сомнительным» из превью: адресуются порядковым номером строки
        // в разобранном файле (ImportRecordPreviewRow.RowIndex) и уезжают в сам payload
        // полем suspect_note — импорт положит такую строку сразу с ручной пометкой.
        var resultsJson = ImportPayloadSuspectFlags.Apply(entry.Parsed.ResultsJson, request.SuspectFlags);

        var jobId = _jobs.Enqueue(
            Encoding.UTF8.GetBytes(resultsJson),
            entry.FileName,
            request.CategoryKeys,
            eventOptions,
            entry.DiscoveredId,
            discoveredOrgCompId);

        // Статус imported проставляет фоновый обработчик после успешного завершения job (A1).
        return Accepted(new { jobId });
    }

    /// <summary>
    /// Ответ превью про клубный зачёт. Когда зачёт есть, а правила под его шкалу нет, отдаём
    /// заготовку для кнопки «Завести правило»: имя версии по конвенции и саму шкалу — админ
    /// видит, что именно заведёт, и правит имя, если нужно.
    /// </summary>
    private static object StandingResponse(OfficialClubStandingProbe? probe, ParsedCompetition parsed)
    {
        if (probe is null)
            return new
            {
                known = false, hasStanding = false, ruleId = (int?)null, ruleVersion = (string?)null,
                canCreateRule = false, suggestedVersion = (string?)null,
                message = "Про официальный клубный зачёт ничего сказать нельзя: нет loglig-id или сайт недоступен."
            };

        var scale = probe.Scale.OrderBy(p => p.Key).ToList();
        var canCreateRule = probe.HasStanding && probe.MatchedRuleId is null && scale.Count > 0;

        return new
        {
            known = true,
            hasStanding = probe.HasStanding,
            ruleId = probe.MatchedRuleId,
            ruleVersion = probe.MatchedRuleVersion,
            scale = scale.Select(p => new { place = p.Key, points = p.Value }),
            canCreateRule,
            suggestedVersion = canCreateRule ? SuggestRuleVersion(scale, parsed) : null,
            message = probe.Message
        };
    }

    /// <summary>
    /// Имя версии по конвенции проекта — «(очки за 1 место)pt.(мест)pl.(год)»
    /// (docs/admin-pages/pointsrules.md): в выпадашке привязки сразу видно шкалу, а не только дату.
    /// </summary>
    private static string SuggestRuleVersion(
        IReadOnlyList<KeyValuePair<int, int>> scale, ParsedCompetition parsed)
    {
        var top = scale[0].Value;
        var places = scale[^1].Key;
        var year = ParseCompetitionYear(parsed) ?? DateTime.UtcNow.Year;
        return $"{top}pt.{places}pl.{year}";
    }

    private static int? ParseCompetitionYear(ParsedCompetition parsed) =>
        DateOnly.TryParseExact(parsed.Competitions.FirstOrDefault()?.Date, "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Year
            : null;

    /// <summary>
    /// Завести правило клубных очков по шкале официального зачёта — прямо из превью, без
    /// похода в /Admin/PointsRules и повторного затягивания.
    ///
    /// Шкалу берём ИЗ КЭША ПРЕВЬЮ, а не из тела запроса: сервер уже снял её с loglig минуту
    /// назад, и сочинить её со стороны клиента быть не должно.
    /// </summary>
    [HttpPost("club-rule")]
    public async Task<IActionResult> CreateClubRule([FromBody] CreateClubRuleRequest request, CancellationToken ct)
    {
        var entry = _previews.GetEntry(request.PreviewId);
        if (entry == null)
            return NotFound(new { error = $"Превью не найдено или истекло ({_previews.EntryLifetime.TotalMinutes:0} мин)" });

        var probe = entry.ClubStanding;
        if (probe is null || !probe.HasStanding || probe.Scale.Count == 0)
            return BadRequest(new { error = "У этого соревнования нет официального зачёта со снятой шкалой" });

        if (probe.MatchedRuleId is int already)
            return BadRequest(new { error = $"Шкала уже совпадает с правилом #{already} — заводить новое незачем" });

        var scale = probe.Scale.OrderBy(p => p.Key).ToList();
        var version = string.IsNullOrWhiteSpace(request.Version)
            ? SuggestRuleVersion(scale, entry.Parsed)
            : request.Version.Trim();

        var year = ParseCompetitionYear(entry.Parsed);
        var name = entry.Parsed.Competitions.FirstOrDefault()?.Competition;

        var input = new PointRuleInputDto
        {
            Version = version,
            EffectiveFrom = new DateOnly(year ?? DateTime.UtcNow.Year, 1, 1),
            Description = $"Заведено из превью затягивания по официальному зачёту loglig: {name}",
            Scope = "all",
            DefaultPoints = 0,
            MaxScoringPlace = scale[^1].Key,
            // ManualOnly обязателен: правило без него сразу входит в автоподбор и перехватывает
            // ВСЕ соревнования без явной привязки — этот баг мы уже ловили.
            ManualOnly = true,
            RelayMultiplier = 2,
            Entries = scale.Select(p => new PointRuleEntryDto { Place = p.Key, Points = p.Value }).ToList()
        };

        var result = await _rules.CreateAsync(PointRuleKind.Clubs, input);
        if (!result.Success) return BadRequest(new { error = result.Error });

        await _audit.LogAsync("pointrule.create", "PointRuleClubs", result.Id.ToString(),
            $"Правило «{version}» заведено из превью затягивания: шкала "
            + string.Join(",", scale.Select(p => p.Value)) + $" ({scale.Count} мест)");

        _logger.LogInformation("Discovery: заведено правило клубных очков {Version} (#{Id}) из превью",
            version, result.Id);
        return Ok(new { ruleId = result.Id, version });
    }

    public sealed record CreateClubRuleRequest(Guid PreviewId, string? Version);

    public sealed record SetStatusRequest(string Status);

    public sealed record SetDisciplineRequest(string Discipline);

    public sealed record DiscoveryImportRequest(
        Guid PreviewId,
        string[]? CategoryKeys,
        int? EventId,
        string? NewEventName,
        bool OverwriteExisting = false,
        bool DeleteMissing = false,
        IReadOnlyList<ImportSuspectFlag>? SuspectFlags = null,
        int? PointRuleClubsId = null,
        // Флаги соревнования из превью: null = «не трогать» (см. ImportEventOptions).
        bool? IsAward = null,
        bool? IsChampionship = null,
        bool? IsMasters = null,
        bool? ClubPointsDisabled = null,
        string? PoolType = null);
}
