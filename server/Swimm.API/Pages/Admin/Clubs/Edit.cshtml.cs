using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Clubs;

/// <summary>Переименование клуба (фаза 7.3 op#2). Пишет аудит; удаление/дедуп — через merge на Index.</summary>
[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly IClubAdminRepository _repo;
    private readonly IAdminAuditService _audit;

    public EditModel(IClubAdminRepository repo, IAdminAuditService audit)
    {
        _repo = repo;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public ClubForm Input { get; set; } = new();

    public ClubEditDto? Existing { get; private set; }

    public string? Error { get; private set; }

    public class ClubForm
    {
        public string Name { get; set; } = "";
        public string NameEn { get; set; } = "";
        public bool IsPseudo { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        Existing = await _repo.GetByIdAsync(Id);
        if (Existing == null)
        {
            TempData["Flash"] = $"Клуб #{Id} не найден";
            return RedirectToPage("Index");
        }
        Input = new ClubForm { Name = Existing.Name, NameEn = Existing.NameEn, IsPseudo = Existing.IsPseudo };
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var before = await _repo.GetByIdAsync(Id);
        if (before == null)
        {
            TempData["Flash"] = $"Клуб #{Id} не найден";
            return RedirectToPage("Index");
        }

        var result = await _repo.UpdateAsync(Id, new ClubInputDto
        {
            Name = Input.Name, NameEn = Input.NameEn, IsPseudo = Input.IsPseudo
        });
        if (!result.Success)
        {
            Error = result.Error;
            Existing = before;
            return Page();
        }

        await _audit.LogAsync("club.update", "Club", Id.ToString(),
            $"Клуб #{Id}: «{before.Name}» → «{Input.Name.Trim()}»",
            new { before = new { before.Name, before.NameEn, before.IsPseudo },
                  after = new { Input.Name, Input.NameEn, Input.IsPseudo } });

        TempData["Flash"] = "Клуб обновлён";
        return RedirectToPage("Edit", new { id = Id });
    }
}
