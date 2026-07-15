using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Repositories;

namespace Swimm.API.Pages.Admin.Users;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IAdminRepository _repo;

    public IndexModel(IAdminRepository repo) => _repo = repo;

    public LoginStatsDto Stats { get; private set; } = new();
    public IReadOnlyList<UserDto> Users { get; private set; } = [];

    /// <summary>Раскрытый пользователь (панель истории логинов), ?userId= в query.</summary>
    [BindProperty(SupportsGet = true)]
    public int? UserId { get; set; }

    public UserDetailDto? Detail { get; private set; }

    public static bool IsOnline(DateTime? lastSeenAt) =>
        lastSeenAt != null && DateTime.UtcNow - lastSeenAt.Value < AdminRepository.OnlineWindow;

    public async Task OnGetAsync()
    {
        // Ретеншн журнала — лениво при открытии панели (дёшево: индекс по LoginAt).
        await _repo.CleanupLoginHistoryAsync();

        Stats = await _repo.GetLoginStatsAsync();
        Users = await _repo.GetUsersAsync();
        if (UserId != null)
            Detail = await _repo.GetUserDetailsAsync(UserId.Value);
    }

    public async Task<IActionResult> OnPostSetActiveAsync(int userId, bool isActive)
    {
        var ok = await _repo.SetUserActiveAsync(userId, isActive);
        TempData["Flash"] = ok
            ? (isActive ? "Пользователь активирован" : "Пользователь отключён, все сессии отозваны")
            : "Пользователь не найден";
        return RedirectToPage(new { userId = UserId });
    }

    public async Task<IActionResult> OnPostForceSignOutAsync(int userId)
    {
        var ok = await _repo.ForceSignOutAsync(userId);
        TempData["Flash"] = ok ? "Все сессии пользователя отозваны" : "Пользователь не найден";
        return RedirectToPage(new { userId = UserId });
    }
}
