using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Controllers;

/// <summary>
/// Свой аккаунт: кнопка «Delete account» на /setup-profile (docs/plans/account-deletion-plan.md).
/// Только своё; ответ личный, поэтому без кэша.
/// </summary>
[ApiController]
[Route("api/me/account")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class AccountController : ControllerBase
{
    private readonly IAccountDeletionService _deletion;

    public AccountController(IAccountDeletionService deletion)
    {
        _deletion = deletion;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    private DateTime? AuthTime() =>
        AccountDeletionRules.ParseAuthTime(User.FindFirstValue(AccountDeletionRules.AuthTimeClaim));

    /// <summary>Что удалится вместе с аккаунтом и можно ли удалить сейчас.</summary>
    [HttpGet("deletion-impact")]
    public async Task<IActionResult> GetDeletionImpact(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var dto = await _deletion.GetImpactAsync(userId.Value, AuthTime(), ct);
        return dto == null ? Unauthorized() : Ok(dto);
    }

    /// <summary>
    /// Удалить свой аккаунт насовсем (Р1). Подтверждение — email плюс пароль или свежий вход (Р3).
    /// Лимит «auth»: пароль здесь проверяется так же, как при входе.
    /// </summary>
    [HttpDelete]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Delete([FromBody] AccountDeletionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _deletion.DeleteSelfAsync(userId.Value, request, AuthTime(), ct);
        if (!result.Deleted)
            return StatusCode(result.Status, new { error = result.Error, code = result.Code });

        // Эта сессия гаснет сразу; остальные — при ре-валидации куки (пользователя больше нет).
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}
