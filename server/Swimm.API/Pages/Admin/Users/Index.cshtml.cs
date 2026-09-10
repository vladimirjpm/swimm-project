using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Repositories;

namespace Swimm.API.Pages.Admin.Users;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IAdminRepository _repo;
    private readonly ISettingsService _settings;
    private readonly IAdminAuditService _audit;

    public IndexModel(IAdminRepository repo, ISettingsService settings, IAdminAuditService audit)
    {
        _repo = repo;
        _settings = settings;
        _audit = audit;
    }

    public LoginStatsDto Stats { get; private set; } = new();
    public IReadOnlyList<UserDto> Users { get; private set; } = [];

    /// <summary>Раскрытый пользователь (панель истории логинов), ?userId= в query.</summary>
    [BindProperty(SupportsGet = true)]
    public int? UserId { get; set; }

    public UserDetailDto? Detail { get; private set; }

    public static bool IsOnline(DateTime? lastSeenAt) =>
        lastSeenAt != null && DateTime.UtcNow - lastSeenAt.Value < AdminRepository.OnlineWindow;

    /// <summary>
    /// Действующий лимит групп — тем же правилом, что проверка при создании
    /// (<see cref="HubGroupCreationRules"/>), чтобы колонка не расходилась с тем, что увидит
    /// пользователь. null — без лимита (админ).
    /// </summary>
    public int? EffectiveHubGroupLimit(string[] roles, int? personalLimit) =>
        HubGroupCreationRules.EffectiveLimit(_settings, roles.Contains("Admin"), roles.Contains("Coach"), personalLimit);

    /// <summary>Откуда взялся действующий лимит — подпись в панели деталей.</summary>
    public static string HubGroupLimitSource(string[] roles, int? personalLimit) =>
        roles.Contains("Admin") ? "админ — без лимита, персональный не действует"
        : personalLimit != null ? "персональный"
        : roles.Contains("Coach") ? $"по роли Coach ({HubGroupCreationRules.MaxPerCoachKey})"
        : $"по роли ({HubGroupCreationRules.MaxPerUserKey})";

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

    /// <summary>Персональный лимит групп: пусто — снять (действует лимит по роли), 0 — запретить.</summary>
    public async Task<IActionResult> OnPostSetHubGroupLimitAsync(int userId, int? hubGroupLimit)
    {
        // Не-число биндер превращает в null, а null здесь значит «снять исключение» — такой
        // ввод не должен молча его снимать. Поэтому сперва ModelState, потом диапазон.
        if (!ModelState.IsValid || hubGroupLimit is < 0 or > HubGroupCreationRules.MaxLimit)
        {
            TempData["Flash"] = $"Лимит — целое число от 0 до {HubGroupCreationRules.MaxLimit}; пусто — по роли";
            return RedirectToPage(new { userId });
        }

        if (!await _repo.SetHubGroupLimitAsync(userId, hubGroupLimit))
        {
            TempData["Flash"] = "Пользователь не найден";
            return RedirectToPage();
        }

        await _audit.LogAsync("user.hubgroup-limit", "User", userId.ToString(),
            $"Персональный лимит групп пользователя #{userId}: {hubGroupLimit?.ToString() ?? "снят (по роли)"}",
            new { userId, hubGroupLimit });

        TempData["Flash"] = hubGroupLimit == null
            ? "Персональный лимит снят — действует лимит по роли"
            : $"Персональный лимит групп: {hubGroupLimit}";
        return RedirectToPage(new { userId });
    }
}
