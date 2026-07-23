using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Styles;

/// <summary>Создание/правка/удаление стиля (фаза 7.2 A). Мутации пишутся в аудит (7.4).</summary>
[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly IStyleAdminRepository _repo;
    private readonly IAdminAuditService _audit;

    public EditModel(IStyleAdminRepository repo, IAdminAuditService audit)
    {
        _repo = repo;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    public bool IsNew => Id is null or 0;

    [BindProperty]
    public StyleForm Input { get; set; } = new();

    public StyleEditDto? Existing { get; private set; }

    public string? Error { get; private set; }

    public class StyleForm
    {
        public string Name { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsNew)
        {
            Existing = await _repo.GetByIdAsync(Id!.Value);
            if (Existing == null) return RedirectToPage("Index");
            Input = new StyleForm { Name = Existing.Name };
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var input = new StyleInputDto { Name = Input.Name };
        var result = IsNew
            ? await _repo.CreateAsync(input)
            : await _repo.UpdateAsync(Id!.Value, input);

        if (!result.Success)
        {
            Error = result.Error;
            if (!IsNew) Existing = await _repo.GetByIdAsync(Id!.Value);
            return Page();
        }

        await _audit.LogAsync(IsNew ? "style.create" : "style.update", "Style",
            result.Id.ToString(),
            IsNew ? $"Создан стиль «{Input.Name.Trim()}»" : $"Стиль #{result.Id} → «{Input.Name.Trim()}»");

        TempData["Flash"] = IsNew ? "Стиль создан" : "Изменения сохранены";
        return RedirectToPage("Edit", new { id = result.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (IsNew) return RedirectToPage("Index");

        var name = (await _repo.GetByIdAsync(Id!.Value))?.Name;
        var result = await _repo.DeleteAsync(Id!.Value);
        if (!result.Success)
        {
            Error = result.Error;
            Existing = await _repo.GetByIdAsync(Id!.Value);
            if (Existing != null) Input = new StyleForm { Name = Existing.Name };
            return Page();
        }

        await _audit.LogAsync("style.delete", "Style", Id!.Value.ToString(),
            $"Удалён стиль «{name}»");

        TempData["Flash"] = "Стиль удалён";
        return RedirectToPage("Index");
    }
}
