using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

/// <summary>
/// Админ-API склейки клубов-дублей (docs/tasks/club-merge-plan.md, фаза B): кандидаты
/// по трём эвристикам, dry-run и применение merge. Merge необратим — применение только
/// явным флагом apply (страница /Admin/Clubs покажет dry-run-план перед этим, фаза C).
/// </summary>
[ApiController]
[Route("api/admin/clubs")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class ClubsAdminController : ControllerBase
{
    private readonly IClubDedupService _dedup;
    private readonly IClubMergeService _merge;
    private readonly IDedupIgnoreService _ignore;
    private readonly ILogger<ClubsAdminController> _logger;

    public ClubsAdminController(
        IClubDedupService dedup,
        IClubMergeService merge,
        IDedupIgnoreService ignore,
        ILogger<ClubsAdminController> logger)
    {
        _dedup = dedup;
        _merge = merge;
        _ignore = ignore;
        _logger = logger;
    }

    public sealed record IgnorePairRequest(int IdA, int IdB);

    /// <summary>Пометить пару «не дубли» — больше не всплывает в кандидатах.</summary>
    [HttpPost("dedup-ignore")]
    public async Task<IActionResult> IgnorePair([FromBody] IgnorePairRequest request, CancellationToken ct)
    {
        try { await _ignore.AddAsync(Swimm.Domain.Entities.DedupEntityType.Club, request.IdA, request.IdB, ct); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        return Ok();
    }

    /// <summary>Вернуть развязанную пару обратно в кандидаты.</summary>
    [HttpPost("dedup-ignore/remove")]
    public async Task<IActionResult> UnignorePair([FromBody] IgnorePairRequest request, CancellationToken ct)
        => await _ignore.RemoveAsync(Swimm.Domain.Entities.DedupEntityType.Club, request.IdA, request.IdB, ct)
            ? Ok()
            : NotFound(new { error = "Пара не найдена в списке развязанных" });

    /// <summary>Список развязанных пар (для блока «Скрытые пары»).</summary>
    [HttpGet("dedup-ignore")]
    public async Task<IActionResult> ListIgnored(CancellationToken ct)
        => Ok(await _ignore.ListAsync(Swimm.Domain.Entities.DedupEntityType.Club, ct));

    /// <summary>Кандидаты на склейку. Считается на лету (сотни клубов — дёшево).</summary>
    [HttpGet("dedup-candidates")]
    public async Task<IActionResult> GetCandidates(CancellationToken ct)
        => Ok(await _dedup.FindCandidatesAsync(ct));

    public sealed record MergeRequest(List<ClubMergePair> Pairs, bool Apply);

    /// <summary>Merge выбранных пар. apply=false — dry-run (план, БД не меняется).</summary>
    [HttpPost("merge")]
    public async Task<IActionResult> Merge([FromBody] MergeRequest request, CancellationToken ct)
    {
        if (request.Pairs is not { Count: > 0 })
            return BadRequest(new { error = "Пары не выбраны" });

        ClubMergeReport report;
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
                "Admin {User} применил merge клубов: {Pairs}",
                User.Identity?.Name ?? "?",
                string.Join("; ", report.Pairs.Select(p => $"{p.CanonicalId}←{p.DuplicateId}:{p.Status}")));
        }

        return Ok(report);
    }
}
