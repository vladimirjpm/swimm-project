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
    private readonly ILogger<SwimmersAdminController> _logger;

    public SwimmersAdminController(
        ISwimmerDedupService dedup,
        ISwimmerMergeService merge,
        IDedupIgnoreService ignore,
        ICacheService cache,
        ILogger<SwimmersAdminController> logger)
    {
        _dedup = dedup;
        _merge = merge;
        _ignore = ignore;
        _cache = cache;
        _logger = logger;
    }

    public sealed record IgnorePairRequest(int IdA, int IdB);

    /// <summary>Пометить пару «не дубли» (например, однофамильцы, плававшие в одном
    /// заплыве) — больше не всплывает в кандидатах на склейку.</summary>
    [HttpPost("dedup-ignore")]
    public async Task<IActionResult> IgnorePair([FromBody] IgnorePairRequest request, CancellationToken ct)
    {
        try { await _ignore.AddAsync(Swimm.Domain.Entities.DedupEntityType.Swimmer, request.IdA, request.IdB, ct); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        return Ok();
    }

    /// <summary>Вернуть развязанную пару обратно в кандидаты.</summary>
    [HttpPost("dedup-ignore/remove")]
    public async Task<IActionResult> UnignorePair([FromBody] IgnorePairRequest request, CancellationToken ct)
        => await _ignore.RemoveAsync(Swimm.Domain.Entities.DedupEntityType.Swimmer, request.IdA, request.IdB, ct)
            ? Ok()
            : NotFound(new { error = "Пара не найдена в списке развязанных" });

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
}
