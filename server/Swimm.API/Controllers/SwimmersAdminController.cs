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
    private readonly IAdminAuditService _audit;
    private readonly IDataQualityService _quality;
    private readonly IDataCheckRunner _checks;
    private readonly ISearchVisibilityService _visibility;
    private readonly ISwimmerClubRefreshService _clubs;
    private readonly ISwimmerClubLogligCheckService _clubCheck;
    private readonly ILogger<SwimmersAdminController> _logger;

    public SwimmersAdminController(
        ISwimmerDedupService dedup,
        ISwimmerMergeService merge,
        IDedupIgnoreService ignore,
        IAdminAuditService audit,
        IDataQualityService quality,
        IDataCheckRunner checks,
        ISearchVisibilityService visibility,
        ISwimmerClubRefreshService clubs,
        ISwimmerClubLogligCheckService clubCheck,
        ILogger<SwimmersAdminController> logger)
    {
        _dedup = dedup;
        _merge = merge;
        _ignore = ignore;
        _audit = audit;
        _quality = quality;
        _checks = checks;
        _visibility = visibility;
        _clubs = clubs;
        _clubCheck = clubCheck;
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
        }

        return Ok(report);
    }

    public sealed record SetGenderRequest(string Gender);

    /// <summary>
    /// Проставить пол пловцу напрямую. Нужен превью затягивания: там подозрительный заплыв
    /// разбирают ДО импорта, реестр проверок по нему ещё не отрабатывал, а находки — единственный
    /// вход /Admin/Health — не существует. Логика та же самая (`SetSwimmerGenderAsync`):
    /// пол уходит пловцу и в его строки БЕЗ пола, напечатанный в протоколе не перезаписывается.
    /// </summary>
    [HttpPost("{id:int}/gender")]
    public async Task<IActionResult> SetGender(int id, [FromBody] SetGenderRequest request, CancellationToken ct)
    {
        var rows = await _checks.SetSwimmerGenderAsync(id, request.Gender, ct);
        if (rows is null)
            return BadRequest(new { error = "Пловец не найден или пол задан неверно (male|female)" });

        await _audit.LogAsync("swimmer.set-gender", "Swimmer", id.ToString(),
            $"Пол '{request.Gender}' проставлен пловцу; строк результата поправлено — {rows}",
            new { swimmerId = id, request.Gender, rows }, ct);
        return Ok(new { rows });
    }

    /// <summary>Пловцы, скрытые из поиска по просьбе (A10 шаг 1, /privacy).</summary>
    [HttpGet("search-hidden")]
    public async Task<IActionResult> ListSearchHidden(CancellationToken ct)
        => Ok(await _visibility.ListHiddenAsync(ct));

    public sealed record SearchHiddenRequest(bool Hidden);

    /// <summary>
    /// Скрыть / вернуть пловца в поиск: <c>noindex</c> на его странице и исключение из
    /// sitemap.xml. Страница и результаты остаются (Р76) — это не удаление.
    /// </summary>
    [HttpPost("{id:int}/search-hidden")]
    public async Task<IActionResult> SetSearchHidden(int id, [FromBody] SearchHiddenRequest request, CancellationToken ct)
    {
        if (!await _visibility.SetHiddenAsync(id, request.Hidden, ct))
            return NotFound(new { error = "Пловец не найден (или это пловец закрытой группы — его и так нет в поиске)" });

        await _audit.LogAsync(request.Hidden ? "swimmer.search-hide" : "swimmer.search-unhide", "Swimmer", id.ToString(),
            request.Hidden ? "Пловец скрыт из поиска (noindex, вне sitemap)" : "Пловец возвращён в поиск",
            new { swimmerId = id, request.Hidden }, ct);
        return Ok();
    }

    /// <summary>
    /// Блок «Клуб пловца» (Р95): клуб в карточке, закреплён ли, что сказало бы правило
    /// «клуб последнего соревнования», периоды клубов по протоколам и последние смены.
    /// </summary>
    [HttpGet("{id:int}/club")]
    public async Task<IActionResult> GetClub(int id, CancellationToken ct)
        => await _clubs.GetForAdminAsync(id, ct) is { } dto ? Ok(dto) : NotFound(new { error = "Пловец не найден" });

    /// <param name="ClubFromMasters">Флаг «клуб по мастерс-стартам» (Р109); null — не трогать.</param>
    public sealed record SetClubRequest(int? ClubId, bool Locked, bool? ClubFromMasters = null);

    /// <summary>
    /// Ручная правка клуба и закрепление (К3). Закреплённый клуб импорт не меняет; без
    /// закрепления следующий импорт может вернуть клуб последнего соревнования. Смена клуба
    /// пишет журнал Sys_SwimmerClubChanges (source=admin) и аудит.
    /// </summary>
    [HttpPost("{id:int}/club")]
    public async Task<IActionResult> SetClub(int id, [FromBody] SetClubRequest request, CancellationToken ct)
    {
        var (result, error) = await _clubs.SetByAdminAsync(id, request.ClubId, request.Locked, request.ClubFromMasters, ct);
        switch (error)
        {
            case SwimmerClubSetError.SwimmerNotFound: return NotFound(new { error = "Пловец не найден" });
            case SwimmerClubSetError.ClubNotFound: return BadRequest(new { error = $"Клуба #{request.ClubId} нет" });
            case SwimmerClubSetError.PseudoClub: return BadRequest(new { error = "Это псевдоклуб (страна/сборная) — не клуб пловца" });
        }

        var r = result!;
        var summary = (r.FromClubId != r.ToClubId
                ? $"Клуб пловца: #{r.FromClubId?.ToString() ?? "—"} → #{r.ToClubId?.ToString() ?? "—"}"
                : $"Клуб пловца не менялся (#{r.ToClubId?.ToString() ?? "—"})")
            + (r.WasLocked != r.Locked ? (r.Locked ? ", закреплён" : ", закрепление снято") : "")
            + (r.WasFromMasters != r.FromMasters ? (r.FromMasters == true ? ", клуб по мастерс-стартам" : ", флаг «по мастерс-стартам» снят") : "");
        await _audit.LogAsync("swimmer.club-set", "Swimmer", id.ToString(), summary, r, ct);
        return Ok(r);
    }

    /// <summary>
    /// «Проверить сейчас» (Р98): открыть карточку loglig заново (суточный кэш мимо) и сверить ждущего
    /// кандидата пловца. Подтверждённая смена сразу записывается (журнал source=loglig + аудит).
    /// 404 — у пловца нет ждущего кандидата.
    /// </summary>
    [HttpPost("{id:int}/club/check")]
    public async Task<IActionResult> CheckClub(int id, CancellationToken ct)
    {
        var verdict = await _clubCheck.CheckSwimmerAsync(id, ct);
        if (verdict is null) return NotFound(new { error = "У пловца нет ждущего кандидата на смену клуба" });
        if (verdict.Applied)
            await _audit.LogAsync("swimmer.club-loglig", "Swimmer", id.ToString(),
                $"Клуб пловца сменён по loglig («{verdict.LogligClubName}»), кнопка «проверить сейчас»", verdict, ct);
        return Ok(verdict);
    }

    /// <summary>Ждущие кандидаты на смену клуба: счёт по вердиктам loglig и до 100 строк каждого.</summary>
    [HttpGet("club-candidates")]
    public async Task<IActionResult> ClubCandidates(CancellationToken ct)
        => Ok(await _clubs.PendingAsync(100, ct));

    /// <summary>Журнал смен клуба за <paramref name="days"/> дней (по умолчанию 30), новые сверху.</summary>
    [HttpGet("club-changes")]
    public async Task<IActionResult> ClubChanges([FromQuery] int days = 30, CancellationToken ct = default)
        => Ok(await _clubs.RecentChangesAsync(Math.Clamp(days, 1, 365), 200, ct));

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
        }

        return Ok(report);
    }
}
