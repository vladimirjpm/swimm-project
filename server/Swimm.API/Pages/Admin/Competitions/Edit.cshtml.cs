using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Competitions;

[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly ICompetitionAdminRepository _repo;
    private readonly IImportService _import;
    private readonly ILogger<EditModel> _logger;

    public EditModel(ICompetitionAdminRepository repo, IImportService import, ILogger<EditModel> logger)
    {
        _repo = repo;
        _import = import;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    public bool IsNew => Id is null or 0;

    [BindProperty]
    public CompetitionForm Input { get; set; } = new();

    // Форма добавления URL результатов
    [BindProperty] public string? NewUrlCulture { get; set; }
    [BindProperty] public string? NewUrl { get; set; }

    // Подтверждение каскадного удаления — вводом названия
    [BindProperty] public string? ConfirmName { get; set; }

    /// <summary>Загруженное соревнование (edit-режим): URL-ы, событие, число результатов.</summary>
    public CompetitionEditDto? Existing { get; private set; }

    /// <summary>Все категории (чекбоксы формы).</summary>
    public IReadOnlyList<CategoryTagDto> AllCategories { get; private set; } = [];

    /// <summary>Ошибка валидации мутации — показывается в форме.</summary>
    public string? Error { get; private set; }

    public class CompetitionForm
    {
        public string Name { get; set; } = "";
        public string? SubName { get; set; }
        public string Date { get; set; } = "";
        public string PoolType { get; set; } = "";
        public string Country { get; set; } = "";
        public int? OrgCompId { get; set; }
        public bool IsAward { get; set; }
        public bool ShowCombineAllResults { get; set; }
        /// <summary>Выбранные категории (IsMasters выводится из категории Masters).</summary>
        public List<string> CategoryKeys { get; set; } = [];
    }

    public async Task<IActionResult> OnGetAsync()
    {
        AllCategories = await _repo.GetAllCategoriesAsync();
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
        AllCategories = await _repo.GetAllCategoriesAsync();
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

        TempData["Flash"] = IsNew ? "Соревнование создано" : "Изменения сохранены";
        return RedirectToPage("Edit", new { id = result.Id });
    }

    public async Task<IActionResult> OnPostAddUrlAsync()
    {
        if (IsNew) return RedirectToPage("Index");

        AllCategories = await _repo.GetAllCategoriesAsync();
        Existing = await _repo.GetByIdAsync(Id!.Value);
        if (Existing == null) return RedirectToPage("Index");

        if (Existing.OrgCompId is not int orgCompId)
        {
            Error = "Сначала задайте OrgCompId и сохраните — URL-ы результатов привязываются к нему.";
            Input = ToForm(Existing);
            return Page();
        }

        var result = await _repo.AddResultUrlAsync(orgCompId, NewUrlCulture ?? "", NewUrl ?? "");
        if (!result.Success)
        {
            Error = result.Error;
            Input = ToForm(Existing);
            return Page();
        }

        TempData["Flash"] = "URL добавлен";
        return RedirectToPage("Edit", new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteUrlAsync(int urlId)
    {
        if (IsNew) return RedirectToPage("Index");

        // Удаляем URL только в рамках текущего соревнования (по его OrgCompId), а не по «голому» urlId.
        Existing = await _repo.GetByIdAsync(Id!.Value);
        if (Existing?.OrgCompId is not int orgCompId) return RedirectToPage("Edit", new { id = Id });

        await _repo.RemoveResultUrlAsync(urlId, orgCompId);
        TempData["Flash"] = "URL удалён";
        return RedirectToPage("Edit", new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (IsNew) return RedirectToPage("Index");

        AllCategories = await _repo.GetAllCategoriesAsync();
        Existing = await _repo.GetByIdAsync(Id!.Value);
        if (Existing == null) return RedirectToPage("Index");

        // Каскадное удаление деструктивно → требуем точный ввод названия.
        if (!string.Equals(ConfirmName?.Trim(), Existing.Name.Trim(), StringComparison.Ordinal))
        {
            Error = "Название введено неверно — удаление отменено.";
            Input = ToForm(Existing);
            return Page();
        }

        var deleted = await _import.DeleteCompetitionAsync(Id!.Value);
        if (deleted == null) return RedirectToPage("Index");

        _logger.LogWarning(
            "Admin {User} каскадно удалил соревнование #{Id} «{Name}»: {Results} результатов, " +
            "{Relays} эстафет, {Galleries} галерей, {ResultUrls} URL, {ImportHistory} записей истории",
            User.Identity?.Name ?? "?", deleted.CompetitionId, deleted.CompetitionName,
            deleted.Results, deleted.Relays, deleted.Galleries, deleted.ResultUrls, deleted.ImportHistory);

        TempData["Flash"] = $"Соревнование «{deleted.CompetitionName}» удалено ({deleted.Results} результатов)";
        return RedirectToPage("Index");
    }

    private static CompetitionForm ToForm(CompetitionEditDto d) => new()
    {
        Name = d.Name,
        SubName = d.SubName,
        Date = d.Date,
        PoolType = d.PoolType,
        Country = d.Country,
        OrgCompId = d.OrgCompId,
        IsAward = d.IsAward,
        ShowCombineAllResults = d.ShowCombineAllResults,
        CategoryKeys = d.CategoryKeys
    };

    private static CompetitionInputDto ToInput(CompetitionForm f) => new()
    {
        Name = f.Name,
        SubName = f.SubName,
        Date = f.Date,
        PoolType = f.PoolType,
        Country = f.Country,
        OrgCompId = f.OrgCompId,
        IsAward = f.IsAward,
        ShowCombineAllResults = f.ShowCombineAllResults,
        CategoryKeys = f.CategoryKeys ?? []
    };
}
