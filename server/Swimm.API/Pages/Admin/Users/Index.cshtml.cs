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
    private readonly IPersonaSeeder _personas;
    private readonly IWebHostEnvironment _env;

    public IndexModel(IAdminRepository repo, ISettingsService settings, IAdminAuditService audit,
        IPersonaSeeder personas, IWebHostEnvironment env)
    {
        _repo = repo;
        _settings = settings;
        _audit = audit;
        _personas = personas;
        _env = env;
    }

    /// <summary>
    /// Кнопка «Сбросить» у тестового персонажа (просьба Влада 17.10.2026) — только Development:
    /// персонажи живут в локальной базе и на прод не переносятся (server/db/seed-tables.txt), а
    /// ProdAdmin пишет в прод-БД.
    /// </summary>
    public bool CanResetPersonas => _env.IsDevelopment();

    /// <summary>Персонаж из <see cref="TestPersonas.All"/> — его можно вернуть в исходное состояние.</summary>
    public static bool IsPersona(string email) =>
        TestPersonas.All.Any(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// Ответ опроса «Who are you?» (/setup-profile) одной строкой — подписями, как их видит
    /// пользователь. null — не отвечал; пустая строка — ответил, сняв все галочки.
    /// </summary>
    public static string? SurveyAnswer(UserDetailDto d)
    {
        if (d.ProfileRolesAnsweredAt == null) return null;
        var picked = new List<string>();
        if (d.ProfileRoleSwimmer) picked.Add("I swim");
        if (d.ProfileRoleParent) picked.Add("I'm a parent");
        if (d.ProfileRoleCoach) picked.Add("I'm a coach");
        if (d.ProfileRoleWatcher) picked.Add("Just watching");
        return string.Join(", ", picked);
    }

    /// <summary>Фильтр списка (?show=): ключ из <see cref="Filters"/>; пусто/чужое — все.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Show { get; set; }

    /// <summary>
    /// Чем тестовый персонаж отличается от исходного состояния: id → что поменялось. Пустой
    /// список — не менялся. Только Development (там же, где кнопка «Сбросить»).
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> PersonaChanges { get; private set; }
        = new Dictionary<int, IReadOnlyList<string>>();

    /// <summary>Персонаж изменён руками — кнопка «Сбросить» горит.</summary>
    public bool IsPersonaChanged(int userId) => PersonaChanges.TryGetValue(userId, out var c) && c.Count > 0;

    /// <summary>
    /// Фильтры списка пользователей (просьба Влада 17.10.2026): ключ → подпись. Порядок — порядок
    /// чипов. «changed» — только в Development: изменённые тестовые персонажи.
    /// </summary>
    public static readonly (string Key, string Label)[] Filters =
    [
        ("all", "Все"),
        ("regular", "Обычные"),
        ("test", "Тестовые"),
        ("changed", "Изменённые тестовые"),
        ("admins", "Админы"),
        ("coaches", "Тренеры"),
        ("online", "Онлайн"),
        ("disabled", "Отключённые"),
    ];

    /// <summary>Подходит ли пользователь под фильтр. Тестовый — email utest…@swimm.test (TestAccountRules).</summary>
    public bool Matches(UserDto u, string? key) => key switch
    {
        "regular" => !TestAccountRules.IsTestEmail(u.Email),
        "test" => TestAccountRules.IsTestEmail(u.Email),
        "changed" => IsPersonaChanged(u.Id),
        "admins" => u.Roles.Contains("Admin"),
        "coaches" => u.Roles.Contains("Coach"),
        "online" => IsOnline(u.LastSeenAt),
        "disabled" => !u.IsActive,
        _ => true,
    };

    /// <summary>Все пользователи до фильтра — для чисел на чипах.</summary>
    public IReadOnlyList<UserDto> AllUsers { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // Ретеншн журнала — лениво при открытии панели (дёшево: индекс по LoginAt).
        await _repo.CleanupLoginHistoryAsync();

        Stats = await _repo.GetLoginStatsAsync();
        if (CanResetPersonas) PersonaChanges = await _personas.GetPersonaChangesAsync();
        if (Show == "changed" && !CanResetPersonas) Show = null;
        AllUsers = await _repo.GetUsersAsync();
        Users = AllUsers.Where(u => Matches(u, Show)).ToList();
        if (UserId != null)
            Detail = await _repo.GetUserDetailsAsync(UserId.Value);
    }

    public async Task<IActionResult> OnPostSetActiveAsync(int userId, bool isActive)
    {
        var ok = await _repo.SetUserActiveAsync(userId, isActive);
        TempData["Flash"] = ok
            ? (isActive ? "Пользователь активирован" : "Пользователь отключён, все сессии отозваны")
            : "Пользователь не найден";
        return RedirectToPage(new { userId = UserId, show = Show });
    }

    public async Task<IActionResult> OnPostForceSignOutAsync(int userId)
    {
        var ok = await _repo.ForceSignOutAsync(userId);
        TempData["Flash"] = ok ? "Все сессии пользователя отозваны" : "Пользователь не найден";
        return RedirectToPage(new { userId = UserId, show = Show });
    }

    /// <summary>
    /// Вернуть тестового персонажа в исходное состояние: стереть всё, что на нём накопилось
    /// руками, и засидить заново (id не меняется). Только Development и только персонажи.
    /// </summary>
    public async Task<IActionResult> OnPostResetPersonaAsync(int userId)
    {
        if (!CanResetPersonas) return NotFound();

        var log = await _personas.ResetPersonaAsync(userId);
        if (log == null)
        {
            TempData["Flash"] = "Это не тестовый персонаж — сбрасывать нечего";
            return RedirectToPage(new { userId = UserId, show = Show });
        }

        await _audit.LogAsync("user.persona-reset", "User", userId.ToString(),
            $"Тестовый персонаж #{userId} возвращён в исходное состояние", new { userId, log });

        TempData["Flash"] = "Персонаж возвращён в исходное состояние";
        return RedirectToPage(new { userId = UserId, show = Show });
    }

    /// <summary>Персональный лимит групп: пусто — снять (действует лимит по роли), 0 — запретить.</summary>
    public async Task<IActionResult> OnPostSetHubGroupLimitAsync(int userId, int? hubGroupLimit)
    {
        // Не-число биндер превращает в null, а null здесь значит «снять исключение» — такой
        // ввод не должен молча его снимать. Поэтому сперва ModelState, потом диапазон.
        if (!ModelState.IsValid || hubGroupLimit is < 0 or > HubGroupCreationRules.MaxLimit)
        {
            TempData["Flash"] = $"Лимит — целое число от 0 до {HubGroupCreationRules.MaxLimit}; пусто — по роли";
            return RedirectToPage(new { userId, show = Show });
        }

        if (!await _repo.SetHubGroupLimitAsync(userId, hubGroupLimit))
        {
            TempData["Flash"] = "Пользователь не найден";
            return RedirectToPage(new { show = Show });
        }

        await _audit.LogAsync("user.hubgroup-limit", "User", userId.ToString(),
            $"Персональный лимит групп пользователя #{userId}: {hubGroupLimit?.ToString() ?? "снят (по роли)"}",
            new { userId, hubGroupLimit });

        TempData["Flash"] = hubGroupLimit == null
            ? "Персональный лимит снят — действует лимит по роли"
            : $"Персональный лимит групп: {hubGroupLimit}";
        return RedirectToPage(new { userId, show = Show });
    }
}
