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
    private readonly IAdminAuditService _audit;
    private readonly IDataQualityService _quality;
    private readonly IClubAdminRepository _clubs;
    private readonly ILogger<ClubsAdminController> _logger;

    public ClubsAdminController(
        IClubDedupService dedup,
        IClubMergeService merge,
        IDedupIgnoreService ignore,
        IAdminAuditService audit,
        IDataQualityService quality,
        IClubAdminRepository clubs,
        ILogger<ClubsAdminController> logger)
    {
        _clubs = clubs;
        _dedup = dedup;
        _merge = merge;
        _ignore = ignore;
        _audit = audit;
        _quality = quality;
        _logger = logger;
    }

    /// <summary>Deep-link выборки «здоровье данных» (T3b): filter=no-swimmers|no-country, топ-200.</summary>
    [HttpGet("quality")]
    public async Task<IActionResult> GetQuality([FromQuery] string filter, CancellationToken ct)
        => Ok(await _quality.GetClubQualityAsync(filter, ct));

    /// <summary>
    /// Удалить пустой клуб (мусор парсера из фильтра «Без пловцов»). Предикат перепроверяет
    /// репозиторий — страница могла отрисовать список до того, как импорт повесил результаты.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteEmpty(int id, CancellationToken ct)
    {
        var result = await _clubs.DeleteEmptyAsync(id, ct);
        if (!result.Success) return BadRequest(new { error = result.Error });

        _logger.LogWarning("Admin {User} удалил пустой клуб #{Id} «{Name}»",
            User.Identity?.Name ?? "?", id, result.Name);
        await _audit.LogAsync("club.delete", "Club", id.ToString(),
            $"Удалён пустой клуб #{id} «{result.Name}»", new { Id = id, result.Name }, ct);
        return Ok();
    }

    /// <summary>
    /// Удалить ВСЕ пустые клубы разом (весь список фильтра «Без пловцов», а не видимые топ-200).
    /// Непрошедшие полную проверку возвращаются в skipped — админ видит, что осталось и почему.
    /// </summary>
    [HttpPost("delete-empty")]
    public async Task<IActionResult> DeleteAllEmpty(CancellationToken ct)
    {
        var result = await _clubs.DeleteAllEmptyAsync(ct);

        if (result.Deleted.Count > 0)
        {
            var namesText = string.Join("; ", result.Deleted.Select(d => $"#{d.Id} «{d.Name}»"));
            _logger.LogWarning("Admin {User} удалил пустые клубы ({Count}): {Clubs}",
                User.Identity?.Name ?? "?", result.Deleted.Count, namesText);
            await _audit.LogAsync("club.delete-empty-all", "Club", null,
                $"Пакетная чистка пустых клубов ({result.Deleted.Count}): {namesText}",
                new { result.Deleted, result.Skipped }, ct);
        }

        return Ok(result);
    }

    public sealed record IgnorePairRequest(int IdA, int IdB);

    /// <summary>Пометить пару «не дубли» — больше не всплывает в кандидатах.</summary>
    [HttpPost("dedup-ignore")]
    public async Task<IActionResult> IgnorePair([FromBody] IgnorePairRequest request, CancellationToken ct)
    {
        try { await _ignore.AddAsync(Swimm.Domain.Entities.DedupEntityType.Club, request.IdA, request.IdB, ct); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        await _audit.LogAsync("club.dedup-ignore", "Club", $"{request.IdA},{request.IdB}",
            $"Пара клубов #{request.IdA}/#{request.IdB} помечена «не дубли»", ct: ct);
        return Ok();
    }

    /// <summary>Вернуть развязанную пару обратно в кандидаты.</summary>
    [HttpPost("dedup-ignore/remove")]
    public async Task<IActionResult> UnignorePair([FromBody] IgnorePairRequest request, CancellationToken ct)
    {
        if (!await _ignore.RemoveAsync(Swimm.Domain.Entities.DedupEntityType.Club, request.IdA, request.IdB, ct))
            return NotFound(new { error = "Пара не найдена в списке развязанных" });
        await _audit.LogAsync("club.dedup-unignore", "Club", $"{request.IdA},{request.IdB}",
            $"Пара клубов #{request.IdA}/#{request.IdB} возвращена в кандидаты", ct: ct);
        return Ok();
    }

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
            var pairsText = string.Join("; ", report.Pairs.Select(p => $"{p.CanonicalId}←{p.DuplicateId}:{p.Status}"));
            _logger.LogWarning("Admin {User} применил merge клубов: {Pairs}", User.Identity?.Name ?? "?", pairsText);
            await _audit.LogAsync("club.merge", "Club", null,
                $"Склейка клубов ({report.Pairs.Count} пар): {pairsText}",
                new { report.Pairs }, ct);
        }

        return Ok(report);
    }
}
