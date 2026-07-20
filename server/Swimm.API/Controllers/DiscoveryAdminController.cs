using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
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
    private readonly ICompetitionDiscoveryProvider _provider;
    private readonly IResultSourceProvider _sourceProvider;
    private readonly ISwimmerNameSyncService _nameSync;
    private readonly IImportJobQueue _jobs;
    private readonly IImportService _import;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DiscoveryAdminController> _logger;

    public DiscoveryAdminController(
        ICompetitionDiscoveryService discovery,
        ICompetitionDiscoveryProvider provider,
        IResultSourceProvider sourceProvider,
        ISwimmerNameSyncService nameSync,
        IImportJobQueue jobs,
        IImportService import,
        IMemoryCache cache,
        ILogger<DiscoveryAdminController> logger)
    {
        _discovery = discovery;
        _provider = provider;
        _sourceProvider = sourceProvider;
        _nameSync = nameSync;
        _jobs = jobs;
        _import = import;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetList(CancellationToken ct)
        => Ok(await _discovery.GetAllAsync(ct));

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        try
        {
            return Ok(await _discovery.SyncAsync(ct));
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

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] SetStatusRequest request, CancellationToken ct)
        => await _discovery.SetStatusAsync(id, request.Status, ct)
            ? Ok(new { ok = true })
            : BadRequest(new { error = "Запись не найдена или статус неизвестен" });

    /// <summary>Скачать PDF-протокол вручную (для существующего Import-флоу или глазами посмотреть).</summary>
    [HttpGet("{id:int}/pdf")]
    [IgnoreAntiforgeryToken] // GET-скачивание файла; мутаций нет
    public async Task<IActionResult> DownloadPdf(int id, [FromQuery] string language = "he", CancellationToken ct = default)
    {
        var (pdf, fileName, error) = await FetchPdfAsync(id, language, refreshIfMissing: false, ct);
        if (pdf is null) return BadRequest(new { error });
        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>«Затянуть»: скачать оба PDF из loglig (HE + EN) и прогнать через парсер → превью.
    /// EN-экспорт может отсутствовать — тогда молча парсим только HE (языки видны в бэйджах).</summary>
    [HttpPost("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, CancellationToken ct = default)
    {
        var (pdfHe, fileNameHe, errorHe) = await FetchPdfAsync(id, "he", refreshIfMissing: true, ct);
        var (pdfEn, fileNameEn, _) = await FetchPdfAsync(id, "en", refreshIfMissing: false, ct);
        if (pdfHe is null && pdfEn is null) return BadRequest(new { error = errorHe });

        // Основной файл — HE (канонические имена); EN вторым даёт LastNameEn/FirstNameEn.
        // Если HE недоступен (маловероятно) — парсим EN-only.
        var language = pdfHe != null ? "he" : "en";
        var primary = pdfHe ?? pdfEn!;
        var primaryName = pdfHe != null ? fileNameHe : fileNameEn;
        var languages = new List<string>();
        if (pdfHe != null) languages.Add("he");
        if (pdfEn != null) languages.Add("en");

        ParsedCompetition parsed;
        try
        {
            using var ms = new MemoryStream(primary);
            using var msEn = pdfHe != null && pdfEn != null ? new MemoryStream(pdfEn) : null;
            parsed = await _sourceProvider.ParseAsync(new ResultSourceRequest(
                ms, primaryName, "IsrOrg",
                IsAward: false, PoolType: null,
                SecondaryStream: msEn, SecondaryFileName: msEn != null ? fileNameEn : null,
                ExtraFiles: null,
                Country: null, Language: language));
        }
        catch (InvalidOperationException ex) when (pdfHe != null && pdfEn != null)
        {
            // Пара не склеилась (разный порядок записей и т.п.) — деградируем до HE-only,
            // EN-имена добираются потом кнопкой «Синхр. языки» после починки.
            _logger.LogWarning(ex, "Discovery: двуязычная пара не склеилась (id={Id}), парсим HE-only", id);
            languages.Remove("en");
            using var ms = new MemoryStream(pdfHe);
            try
            {
                parsed = await _sourceProvider.ParseAsync(new ResultSourceRequest(
                    ms, fileNameHe, "IsrOrg",
                    IsAward: false, PoolType: null,
                    SecondaryStream: null, SecondaryFileName: null, ExtraFiles: null,
                    Country: null, Language: "he"));
            }
            catch (InvalidOperationException ex2)
            {
                return BadRequest(new { error = ex2.Message });
            }
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        if (parsed.ResultCount == 0)
            return BadRequest(new { error = "Парсер не распознал ни одного результата — формат протокола изменился? (B4)" });

        await _discovery.AddLanguagesAsync(id, languages, ct);

        var previewId = Guid.NewGuid();
        _cache.Set(PreviewCacheKey(previewId),
            new DiscoveryPreviewEntry(parsed, primaryName, id), TimeSpan.FromMinutes(15));

        var existingMatches = await _import.FindExistingCompetitionsAsync(parsed.Competitions);
        var existingCompetitionId = existingMatches.FirstOrDefault(m => m.ExistingCompetitionId != null)?.ExistingCompetitionId;

        return Ok(new
        {
            previewId,
            format = parsed.Format,
            resultCount = parsed.ResultCount,
            competitions = parsed.Competitions,
            warnings = parsed.Warnings,
            languages,
            existingCompetitionId,
            existingCompetitions = existingMatches
        });
    }

    /// <summary>«Синхронизировать языки»: скачать оба PDF, склеить пару и дозаполнить
    /// EN/HE-имена пловцов в БД по уже импортированным результатам (без переимпорта).</summary>
    [HttpPost("{id:int}/sync-languages")]
    public async Task<IActionResult> SyncLanguages(int id, CancellationToken ct = default)
    {
        // Ошибки пишем в LastError записи — тост в админке живёт секунды, строка таблицы — нет.
        _logger.LogInformation("Discovery sync-languages: старт (id={Id})", id);

        var (pdfHe, fileNameHe, errorHe) = await FetchPdfAsync(id, "he", refreshIfMissing: true, ct);
        if (pdfHe is null) return await SyncFailedAsync(id, $"HE-протокол недоступен: {errorHe}", ct);
        var (pdfEn, fileNameEn, errorEn) = await FetchPdfAsync(id, "en", refreshIfMissing: false, ct);
        if (pdfEn is null) return await SyncFailedAsync(id, $"EN-экспорт недоступен: {errorEn}", ct);

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
        var key = PreviewCacheKey(request.PreviewId);
        if (!_cache.TryGetValue(key, out DiscoveryPreviewEntry? entry) || entry == null)
            return NotFound(new { error = "Превью не найдено или истекло (15 мин)" });

        _cache.Remove(key);

        ImportEventOptions? eventOptions = null;
        if (request.EventId.HasValue || !string.IsNullOrWhiteSpace(request.NewEventName) || request.OverwriteExisting)
            eventOptions = new ImportEventOptions(request.EventId, request.NewEventName, request.OverwriteExisting);

        var jobId = _jobs.Enqueue(
            Encoding.UTF8.GetBytes(entry.Parsed.ResultsJson),
            entry.FileName,
            request.CategoryKeys,
            eventOptions,
            entry.DiscoveredId);

        // Статус imported проставляет фоновый обработчик после успешного завершения job (A1).
        return Accepted(new { jobId });
    }

    private async Task<(byte[]? pdf, string fileName, string? error)> FetchPdfAsync(
        int id, string language, bool refreshIfMissing, CancellationToken ct)
    {
        var all = await _discovery.GetAllAsync(ct);
        var row = all.FirstOrDefault(d => d.Id == id);
        if (row is null) return (null, "", "Запись не найдена");

        var logligId = row.LogligId;
        if (logligId is null)
        {
            if (!refreshIfMissing)
                return (null, "", "Детали не загружены — нажмите «Затянуть» (нет loglig-id).");

            // Детали могли ещё не загружаться — пробуем один раз.
            var refreshed = await _discovery.RefreshDetailsAsync(id, ct);
            logligId = refreshed?.LogligId;
            if (logligId is null)
                return (null, "", refreshed?.LastError ?? "Результаты не опубликованы (нет loglig-id).");
        }

        var culture = language == "en" ? "en-US" : "he-IL";
        try
        {
            var pdf = await _provider.FetchResultsPdfAsync(logligId.Value, culture, ct);
            return (pdf, $"isrorg-{row.OrgCompId}-loglig-{logligId}-{language}.pdf", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Discovery: не удалось скачать PDF logligId={LogligId}", logligId);
            return (null, "", ex.Message);
        }
    }

    private static string PreviewCacheKey(Guid previewId) => $"discovery-preview:{previewId}";

    private sealed record DiscoveryPreviewEntry(ParsedCompetition Parsed, string FileName, int DiscoveredId);

    public sealed record SetStatusRequest(string Status);

    public sealed record DiscoveryImportRequest(
        Guid PreviewId,
        string[]? CategoryKeys,
        int? EventId,
        string? NewEventName,
        bool OverwriteExisting = false);
}
