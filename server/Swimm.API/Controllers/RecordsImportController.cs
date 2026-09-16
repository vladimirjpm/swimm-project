using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

/// <summary>
/// Этап 2.6 — обновление рекордов из URL-источников (World Aquatics / isr.org.il).
/// Fetch только парсит + строит дифф (см. <see cref="IRecordDiffService"/>); запись в БД —
/// только через Apply, отдельным явным шагом (превью перед применением, как договорено в
/// docs/tasks/phase2.6-records-import-sonnet.md).
/// </summary>
[ApiController]
[Route("api/admin/records")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class RecordsImportController : ControllerBase
{
    private readonly IReadOnlyDictionary<string, IRecordSourceProvider> _providers;
    private readonly IRecordDiffService _diffService;
    private readonly IRecordQualityService _quality;
    private readonly IRecordSourceLinksProvider _links;
    private readonly IRecordCountriesProvider _countries;
    private readonly IRecordCountryRunQueue _runs;

    public RecordsImportController(
        IEnumerable<IRecordSourceProvider> providers,
        IRecordDiffService diffService,
        IRecordQualityService quality,
        IRecordSourceLinksProvider links,
        IRecordCountriesProvider countries,
        IRecordCountryRunQueue runs)
    {
        _providers = providers.ToDictionary(p => p.Source, StringComparer.OrdinalIgnoreCase);
        _diffService = diffService;
        _quality = quality;
        _links = links;
        _countries = countries;
        _runs = runs;
    }

    [HttpGet("source-status")]
    public async Task<IActionResult> GetSourceStatus()
        => Ok(await _diffService.GetSourceStatusAsync());

    /// <summary>
    /// Что именно скачает Fetch с isr.org.il: ссылки на PDF-справочники, найденные на
    /// странице «שיאי ישראל». Ходит в сеть, поэтому недоступность источника — не 500,
    /// а мягкий ответ с текстом ошибки: карточки в UI просто не покажут список файлов.
    /// </summary>
    [HttpGet("isrorg-links")]
    public async Task<IActionResult> GetIsrOrgLinks()
    {
        try
        {
            var links = await _links.GetLinksAsync(HttpContext.RequestAborted);
            return Ok(new { pageUrl = _links.PageUrl, links });
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return Ok(new { pageUrl = _links.PageUrl, links = Array.Empty<object>(), error = ex.Message });
        }
    }

    /// <summary>
    /// Сверка справочника рекордов с нашими протоколами (docs/plans/records-quality-plan.md).
    /// Пересчитывает Sys_RecordVerifications целиком.
    ///
    /// ⚠ «Не найдено» — не приговор источнику: протоколы загружены не за все годы.
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify()
        => Ok(await _quality.VerifyAllAsync(HttpContext.RequestAborted));

    [HttpPost("fetch")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Fetch(
        [FromForm] string source,
        [FromForm] IFormFile? primaryFile = null,
        [FromForm] IFormFile? secondaryFile = null,
        [FromForm] string? poolType = null)
    {
        if (!_providers.TryGetValue(source, out var provider))
            return BadRequest(new { error = $"Неизвестный источник '{source}'. Доступны: {string.Join(", ", _providers.Keys)}" });

        var request = new RecordSourceRequest(
            source,
            primaryFile?.OpenReadStream(),
            primaryFile?.FileName,
            secondaryFile?.OpenReadStream(),
            secondaryFile?.FileName,
            poolType);

        IReadOnlyList<ParsedRecordDto> parsed;
        try
        {
            parsed = await provider.FetchAsync(request, HttpContext.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = $"Не удалось скачать источник: {ex.Message}" });
        }

        if (parsed.Count == 0)
            return BadRequest(new { error = "Источник разобран, но не дал ни одной строки — проверьте файл/URL." });

        var diff = await _diffService.BuildDiffAsync(source, parsed, ct: HttpContext.RequestAborted);
        return Ok(diff);
    }

    /// <summary>
    /// Страны источника для выбора в админке (11.1.1): 235 реальных, псевдо-сборные отсеяны.
    /// Ходит в сеть, поэтому недоступность источника — мягкий ответ с текстом ошибки, как у
    /// ссылок isr.org.il: список стран не должен ронять страницу целиком.
    /// </summary>
    [HttpGet("countries")]
    public async Task<IActionResult> GetCountries()
    {
        try
        {
            var countries = await _countries.GetCountriesAsync(HttpContext.RequestAborted);
            return Ok(new { countries, skipped = RecordCountryRun.SkippedCode });
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return Ok(new { countries = Array.Empty<RecordCountryDto>(), error = ex.Message });
        }
    }

    /// <summary>
    /// Запустить прогон по странам (11.1.2). Отвечает сразу: 470 файлов качаются час-два,
    /// ход прогона смотреть через <see cref="GetCountryRun"/>, применять — обычным Apply по
    /// <c>DiffId</c> из результата.
    ///
    /// Пустой список кодов — все страны источника. Израиль в прогон не попадает никогда
    /// (инвариант 2 плана): его обновляет только <c>--records-refresh</c>.
    /// </summary>
    [HttpPost("countries/run")]
    public IActionResult StartCountryRun([FromBody] RecordCountryRunRequest? request)
        => Ok(new { runId = _runs.Enqueue(request?.Codes) });

    /// <summary>Ход прогона: сколько стран пройдено, кто упал, готов ли дифф.</summary>
    [HttpGet("countries/run/{runId:guid}")]
    public IActionResult GetCountryRun(Guid runId)
        => _runs.GetStatus(runId) is { } status
            ? Ok(status)
            : NotFound(new { error = "Прогон не найден: он живёт в памяти процесса и теряется при рестарте." });

    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] RecordDiffApplyRequest request)
    {
        var result = await _diffService.ApplyAsync(request, HttpContext.RequestAborted);
        if (!result.Success) return BadRequest(new { error = result.Error });

        // Подозрительные значения записаны как в источнике, но заведены в реестр кандидатами —
        // админ должен узнать об этом сразу, а не при следующем заходе на дашборд.
        var message = result.CandidatesCreated > 0
            ? $"Применено: {result.AppliedCount}. В реестр спорных — кандидатов: {result.CandidatesCreated} (/Admin/Records?tab=issues)"
            : $"Применено: {result.AppliedCount}";
        return Ok(new { message, applied = result.AppliedCount, candidates = result.CandidatesCreated });
    }
}
