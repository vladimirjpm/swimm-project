using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.HubGroups;

[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly IHubGroupAdminService _service;

    public EditModel(IHubGroupAdminService service) => _service = service;

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    public bool IsNew => Id is null or 0;

    [BindProperty]
    public HubGroupForm Input { get; set; } = new();

    // Поиск пловца для добавления участника. Имя параметра НЕ переопределяем: тег-хелпер
    // <input asp-for="SwimmerQuery"> и скрытые поля формы шлют "SwimmerQuery" — с Name="swq"
    // байндинг не срабатывал и поиск всегда был пустым.
    [BindProperty(SupportsGet = true)]
    public string? SwimmerQuery { get; set; }

    // Добавление участника
    [BindProperty] public int NewSwimmerId { get; set; }
    [BindProperty] public string NewRole { get; set; } = "member";

    /// <summary>Загруженная группа (edit-режим): участники, владелец.</summary>
    public HubGroupEditDto? Existing { get; private set; }

    public IReadOnlyList<ClubOptionDto> AllClubs { get; private set; } = [];

    public IReadOnlyList<SwimmerSearchResultDto> SearchResults { get; private set; } = [];

    /// <summary>Ошибка валидации мутации — показывается в форме.</summary>
    public string? Error { get; private set; }

    public class HubGroupForm
    {
        public string Name { get; set; } = "";
        public string? NameEn { get; set; }
        public string? Slug { get; set; }
        public string? Description { get; set; }
        public string? IconUrl { get; set; }
        public string? CoverImageUrl { get; set; }
        public string? Location { get; set; }
        /// <summary>Alpha-3 код страны (ISR…); пусто — без страны.</summary>
        public string? Country { get; set; }
        public int? ClubId { get; set; }
        public bool IsPublic { get; set; } = true;
        public string? LinkWhatsapp { get; set; }
        public string? LinkTelegram { get; set; }
        public string? LinkInstagram { get; set; }
        public string? LinkSite { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        AllClubs = await _service.GetClubOptionsAsync();

        if (!IsNew)
        {
            Existing = await _service.GetByIdAsync(Id!.Value);
            if (Existing == null) return RedirectToPage("Index");
            Input = ToForm(Existing);
        }

        if (!string.IsNullOrWhiteSpace(SwimmerQuery))
            SearchResults = await _service.SearchSwimmersAsync(SwimmerQuery);

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        AllClubs = await _service.GetClubOptionsAsync();
        var input = ToInput(Input);

        var result = IsNew
            ? await _service.CreateAsync(input, CurrentUserId())
            : await _service.UpdateAsync(Id!.Value, input);

        if (!result.Success)
        {
            Error = result.Error;
            if (!IsNew) Existing = await _service.GetByIdAsync(Id!.Value);
            return Page();
        }

        TempData["Flash"] = IsNew ? "Группа создана" : "Изменения сохранены";
        return RedirectToPage("Edit", new { id = result.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (IsNew) return RedirectToPage("Index");

        var result = await _service.DeleteAsync(Id!.Value);
        TempData["Flash"] = result.Success ? "Группа удалена" : result.Error;
        return result.Success ? RedirectToPage("Index") : RedirectToPage("Edit", new { id = Id });
    }

    public async Task<IActionResult> OnPostAddMemberAsync()
    {
        if (IsNew) return RedirectToPage("Index");

        var result = await _service.AddMemberAsync(Id!.Value, NewSwimmerId, NewRole);
        if (!result.Success)
        {
            AllClubs = await _service.GetClubOptionsAsync();
            Existing = await _service.GetByIdAsync(Id!.Value);
            if (Existing != null) Input = ToForm(Existing);
            if (!string.IsNullOrWhiteSpace(SwimmerQuery))
                SearchResults = await _service.SearchSwimmersAsync(SwimmerQuery);
            Error = result.Error;
            return Page();
        }

        TempData["Flash"] = "Участник добавлен";
        return RedirectToPage("Edit", new { id = Id });
    }

    public async Task<IActionResult> OnPostUpdateMemberAsync(int memberId, string role, int sortOrder)
    {
        if (IsNew) return RedirectToPage("Index");

        await _service.UpdateMemberAsync(Id!.Value, memberId, role, sortOrder);
        TempData["Flash"] = "Роль участника изменена";
        return RedirectToPage("Edit", new { id = Id });
    }

    public async Task<IActionResult> OnPostRemoveMemberAsync(int memberId)
    {
        if (IsNew) return RedirectToPage("Index");

        await _service.RemoveMemberAsync(Id!.Value, memberId);
        TempData["Flash"] = "Участник удалён";
        return RedirectToPage("Edit", new { id = Id });
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private static HubGroupForm ToForm(HubGroupEditDto d)
    {
        string? Find(string kind) => d.Links.FirstOrDefault(l => l.Kind == kind)?.Url;

        return new HubGroupForm
        {
            Name = d.Name,
            NameEn = d.NameEn,
            Slug = d.Slug,
            Description = d.Description,
            IconUrl = d.IconUrl,
            CoverImageUrl = d.CoverImageUrl,
            Location = d.Location,
            Country = d.Country,
            ClubId = d.ClubId,
            IsPublic = d.IsPublic,
            LinkWhatsapp = Find("whatsapp"),
            LinkTelegram = Find("telegram"),
            LinkInstagram = Find("instagram"),
            LinkSite = Find("site"),
        };
    }

    private static HubGroupInputDto ToInput(HubGroupForm f)
    {
        var links = new List<HubGroupLinkDto>();
        void AddLink(string kind, string? url)
        {
            if (!string.IsNullOrWhiteSpace(url)) links.Add(new HubGroupLinkDto { Kind = kind, Url = url });
        }
        AddLink("whatsapp", f.LinkWhatsapp);
        AddLink("telegram", f.LinkTelegram);
        AddLink("instagram", f.LinkInstagram);
        AddLink("site", f.LinkSite);

        return new HubGroupInputDto
        {
            Name = f.Name,
            NameEn = f.NameEn,
            Slug = f.Slug ?? "",
            Description = f.Description,
            IconUrl = f.IconUrl,
            CoverImageUrl = f.CoverImageUrl,
            Location = f.Location,
            // Select всегда постится: незаполненный придёт "" — явное «снять страну» (не null).
            Country = f.Country ?? "",
            ClubId = f.ClubId,
            IsPublic = f.IsPublic,
            Links = links,
        };
    }
}
