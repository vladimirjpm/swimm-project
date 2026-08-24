using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Categories;

[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly ICategoryAdminRepository _repo;

    public EditModel(ICategoryAdminRepository repo) => _repo = repo;

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    public bool IsNew => Id is null or 0;

    [BindProperty]
    public CategoryForm Input { get; set; } = new();

    /// <summary>Загруженная категория (edit-режим): зарезервирован ли Key, число соревнований.</summary>
    public CategoryEditDto? Existing { get; private set; }

    /// <summary>Ошибка валидации мутации — показывается в форме.</summary>
    public string? Error { get; private set; }

    public class CategoryForm
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string? NameHe { get; set; }
        public string? Badge { get; set; }
        public int DisplayOrder { get; set; }

        /// <summary>Возрастная полоса: Kids 8–11, Young 11–14, Juniors 14–17, Adults 17+.
        /// Пусто — категория не про возраст; пустой «по» — полоса открыта сверху.</summary>
        public int? MinAge { get; set; }
        public int? MaxAge { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsNew)
        {
            Existing = await _repo.GetByIdAsync(Id!.Value);
            if (Existing == null) return RedirectToPage("Index");
            Input = ToForm(Existing);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var input = ToInput(Input);
        var result = IsNew
            ? await _repo.CreateAsync(input)
            : await _repo.UpdateAsync(Id!.Value, input);

        if (!result.Success)
        {
            Error = result.Error;
            if (!IsNew) Existing = await _repo.GetByIdAsync(Id!.Value);
            return Page();
        }

        TempData["Flash"] = IsNew ? "Категория создана" : "Изменения сохранены";
        return RedirectToPage("Edit", new { id = result.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (IsNew) return RedirectToPage("Index");

        var result = await _repo.DeleteAsync(Id!.Value);
        if (!result.Success)
        {
            Error = result.Error;
            Existing = await _repo.GetByIdAsync(Id!.Value);
            if (Existing != null) Input = ToForm(Existing);
            return Page();
        }

        TempData["Flash"] = "Категория удалена";
        return RedirectToPage("Index");
    }

    private static CategoryForm ToForm(CategoryEditDto d) => new()
    {
        Key = d.Key,
        Name = d.Name,
        NameHe = d.NameHe,
        Badge = d.Badge,
        DisplayOrder = d.DisplayOrder,
        MinAge = d.MinAge,
        MaxAge = d.MaxAge
    };

    private static CategoryInputDto ToInput(CategoryForm f) => new()
    {
        Key = f.Key,
        Name = f.Name,
        NameHe = f.NameHe,
        Badge = f.Badge,
        DisplayOrder = f.DisplayOrder,
        MinAge = f.MinAge,
        MaxAge = f.MaxAge
    };
}
