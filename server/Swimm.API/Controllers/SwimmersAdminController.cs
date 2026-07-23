using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

/// <summary>
/// Админ-API склейки пловцов-дублей (фаза 7.2): кандидаты (порт dedup-report),
/// dry-run и применение merge. Merge необратим — применение только явным флагом apply
/// с фронта (страница /Admin/Swimmers показывает dry-run-план перед этим).
/// </summary>
[ApiController]
[Route("api/admin/swimmers")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class SwimmersAdminController : ControllerBase
{
    private readonly ISwimmerDedupService _dedup;
    private readonly ISwimmerMergeService _merge;
    private readonly IDedupIgnoreService _ignore;
    private readonly ICacheService _cache;
    private readonly IAdminAuditService _audit;
    private readonly IDataQualityService _quality;
    private readonly ILogger<SwimmersAdminController> _logger;

    public SwimmersAdminController(
        ISwimmerDedupService dedup,
        ISwimmerMergeService merge,
        IDedupIgnoreService ignore,
        ICacheService cache,
        IAdminAuditService audit,
        IDataQualityService quality,
        ILogger<SwimmersAdminController> logger)
    {
        _dedup = dedup;
        _merge = merge;
        _ignore = ignore;
        _cache = cache;
        _audit = audit;
        _quality = quality;
        _logger = logger;
    }

    /// <summary>Deep-link выборки «здоровье данных» (T3b): filter=no-org-id|no-results, топ-200.</summary>
    [HttpGet("quality")]
    public async Task<IActionResult> GetQuality([FromQuery] string filter, CancellationToken ct)
        => Ok(await _quality.GetSwimmerQualityAsync(filter, ct));

    public sealed record IgnorePairRequest(int IdA, int IdB);

    /// <summary>Пометить пару «не дубли» (например, однофамильцы, плававшие в одном
    /// заплыве) — больше не всплывает в кандидатах на склейку.</summary>
    [HttpPost("dedup-ignore")]
    public async Task<IActionResult> IgnorePair([FromBody] IgnorePairRequest request, CancellationToken ct)
    {
        try { await _ignore.AddAsync(Swimm.Domain.Entities.DedupEntityType.Swimmer, request.IdA, request.IdB, ct); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        await _audit.LogAsync("swimmer.dedup-ignore", "Swimmer", $"{request.IdA},{request.IdB}",
            $"Пара пловцов #{request.IdA}/#{request.IdB} помечена «не дубли»", ct: ct);
        return Ok();
    }

    /// <summary>Вернуть развязанную пару обратно в кандидаты.</summary>
    [HttpPost("dedup-ignore/remove")]
    public async Task<IActionResult> UnignorePair([FromBody] IgnorePairRequest request, CancellationToken ct)
    {
        if (!await _ignore.RemoveAsync(Swimm.Domain.Entities.DedupEntityType.Swimmer, request.IdA, request.IdB, ct))
            return NotFound(new { error = "Пара не найдена в списке развязанных" });
        await _audit.LogAsync("swimmer.dedup-unignore", "Swimmer", $"{request.IdA},{request.IdB}",
            $"Пара пловцов #{request.IdA}/#{request.IdB} возвращена в кандидаты", ct: ct);
        return Ok();
    }

    /// <summary>Список развязанных пар (для блока «Скрытые пары»).</summary>
    [HttpGet("dedup-ignore")]
    public async Task<IActionResult> ListIgnored(CancellationToken ct)
        => Ok(await _ignore.ListAsync(Swimm.Domain.Entities.DedupEntityType.Swimmer, ct));

    /// <summary>Кандидаты на склейку + сироты. Считается на лету (тысячи пловцов — дёшево).</summary>
    [HttpGet("dedup-candidates")]
    public async Task<IActionResult> GetCandidates(CancellationToken ct)
        => Ok(await _dedup.FindCandidatesAsync(ct));

    public sealed record MergeRequest(List<SwimmerMergePair> Pairs, bool Apply);

    /// <summary>Merge выбранных пар. apply=false — dry-run (план, БД не меняется).</summary>
    [HttpPost("merge")]
    public async Task<IActionResult> Merge([FromBody] MergeRequest request, CancellationToken ct)
    {
        if (request.Pairs is not { Count: > 0 })
            return BadRequest(new { error = "Пары не выбраны" });

        SwimmerMergeReport report;
        try
        {
            report = await _merge.MergeAsync(request.Pairs, dryRun: !request.Apply, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        if (request.Apply)
        {
            var pairsText = string.Join("; ", report.Pairs.Select(p => $"{p.CanonicalId}←{p.DuplicateId}:{p.Status}"));
            _logger.LogWarning("Admin {User} применил merge пловцов: {Pairs}", User.Identity?.Name ?? "?", pairsText);
            await _audit.LogAsync("swimmer.merge", "Swimmer", null,
                $"Склейка пловцов ({report.Pairs.Count} пар): {pairsText}",
                new { report.Pairs }, ct);
            // Имена пловцов денормализованы в публичных выдачах — сбрасываем кэш целиком.
            await _cache.InvalidateAllAsync();
        }

        return Ok(report);
    }

    public sealed record DeleteOrphansRequest(List<int>? Ids);

    /// <summary>Массовое удаление сирот (B2). Критерий пересчитывается на сервере —
    /// ids из тела не является истиной, см. ISwimmerDedupService.DeleteOrphansAsync.</summary>
    [HttpPost("orphans/delete")]
    public async Task<IActionResult> DeleteOrphans([FromBody] DeleteOrphansRequest? request, CancellationToken ct)
    {
        var report = await _dedup.DeleteOrphansAsync(request?.Ids, ct);

        if (report.Deleted > 0)
        {
            var ids = string.Join(",", report.DeletedIds);
            _logger.LogWarning("Admin {User} удалил {Count} пловцов-сирот: {Ids}",
                User.Identity?.Name ?? "?", report.Deleted, ids);
            await _audit.LogAsync("swimmer.orphans-delete", "Swimmer", null,
                $"Удалено пловцов-сирот: {report.Deleted}",
                new { report.Deleted, report.DeletedIds }, ct);
            await _cache.InvalidateAllAsync();
        }

        return Ok(report);
    }
}
