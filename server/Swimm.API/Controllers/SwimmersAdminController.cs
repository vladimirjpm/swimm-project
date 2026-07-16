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
    private readonly ICacheService _cache;
    private readonly ILogger<SwimmersAdminController> _logger;

    public SwimmersAdminController(
        ISwimmerDedupService dedup,
        ISwimmerMergeService merge,
        ICacheService cache,
        ILogger<SwimmersAdminController> logger)
    {
        _dedup = dedup;
        _merge = merge;
        _cache = cache;
        _logger = logger;
    }

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
            _logger.LogWarning(
                "Admin {User} применил merge пловцов: {Pairs}",
                User.Identity?.Name ?? "?",
                string.Join("; ", report.Pairs.Select(p => $"{p.CanonicalId}←{p.DuplicateId}:{p.Status}")));
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
            _logger.LogWarning(
                "Admin {User} удалил {Count} пловцов-сирот: {Ids}",
                User.Identity?.Name ?? "?", report.Deleted, string.Join(",", report.DeletedIds));
            await _cache.InvalidateAllAsync();
        }

        return Ok(report);
    }

    /// <summary>Лёгкая сводка для карточек «Требует внимания» на дашборде.</summary>
    [HttpGet("attention-summary")]
    public async Task<IActionResult> GetAttentionSummary(CancellationToken ct)
    {
        var report = await _dedup.FindCandidatesAsync(ct);
        return Ok(new SwimmerAttentionSummary(
            report.Orphans.Count,
            report.Candidates.Count(c => c.Sure),
            report.Candidates.Count(c => !c.Sure)));
    }
}
