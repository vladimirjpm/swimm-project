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
    private readonly IImportJobQueue _jobs;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DiscoveryAdminController> _logger;

    public DiscoveryAdminController(
        ICompetitionDiscoveryService discovery,
        ICompetitionDiscoveryProvider provider,
        IResultSourceProvider sourceProvider,
        IImportJobQueue jobs,
        IMemoryCache cache,
        ILogger<DiscoveryAdminController> logger)
    {
        _discovery = discovery;
        _provider = provider;
        _sourceProvider = sourceProvider;
        _jobs = jobs;
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
        var (pdf, fileName, error) = await FetchPdfAsync(id, language, ct);
        if (pdf is null) return BadRequest(new { error });
        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>«Затянуть»: скачать PDF из loglig и прогнать через парсер → превью (как parse-pdf).</summary>
    [HttpPost("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, [FromQuery] string language = "he", CancellationToken ct = default)
    {
        var (pdf, fileName, error) = await FetchPdfAsync(id, language, ct);
        if (pdf is null) return BadRequest(new { error });

        ParsedCompetition parsed;
        try
        {
            using var ms = new MemoryStream(pdf);
            parsed = await _sourceProvider.ParseAsync(new ResultSourceRequest(
                ms, fileName, "IsrOrg",
                IsAward: false, PoolType: null,
                SecondaryStream: null, SecondaryFileName: null, ExtraFiles: null,
                Country: null, Language: language));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        if (parsed.ResultCount == 0)
            return BadRequest(new { error = "Парсер не распознал ни одного результата — формат протокола изменился? (B4)" });

        var previewId = Guid.NewGuid();
        _cache.Set(PreviewCacheKey(previewId),
            new DiscoveryPreviewEntry(parsed, fileName, id), TimeSpan.FromMinutes(15));

        return Ok(new
        {
            previewId,
            format = parsed.Format,
            resultCount = parsed.ResultCount,
            competitions = parsed.Competitions,
            warnings = parsed.Warnings
        });
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
        if (request.EventId.HasValue || !string.IsNullOrWhiteSpace(request.NewEventName))
            eventOptions = new ImportEventOptions(request.EventId, request.NewEventName);

        var jobId = _jobs.Enqueue(
            Encoding.UTF8.GetBytes(entry.Parsed.ResultsJson),
            entry.FileName,
            request.CategoryKeys,
            eventOptions);

        await _discovery.SetStatusAsync(entry.DiscoveredId, "imported", ct);
        return Accepted(new { jobId });
    }

    private async Task<(byte[]? pdf, string fileName, string? error)> FetchPdfAsync(
        int id, string language, CancellationToken ct)
    {
        var all = await _discovery.GetAllAsync(ct);
        var row = all.FirstOrDefault(d => d.Id == id);
        if (row is null) return (null, "", "Запись не найдена");

        var logligId = row.LogligId;
        if (logligId is null)
        {
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
        string? NewEventName);
}
