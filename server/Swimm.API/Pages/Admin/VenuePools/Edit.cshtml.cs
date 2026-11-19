using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.VenuePools;

/// <summary>
/// Создание/правка/удаление места справочника бассейнов (П4). Каждое сохранение — подтверждение
/// длины текущим админом (кто и когда). Мутации пишутся в аудит; антифорджери — штатный Razor Pages.
/// </summary>
[Authorize(Roles = "Admin")]
public class EditModel(IVenuePoolAdminRepository repo, IAdminAuditService audit) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    /// <summary>Предзаполнить название (ссылка «добавить» из списка неизвестных мест).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Venue { get; set; }

    public bool IsNew => Id is null or 0;

    [BindProperty]
    public VenueForm Input { get; set; } = new();

    public VenuePoolRowDto? Existing { get; private set; }

    public string? Error { get; private set; }

    public IReadOnlyList<string> PoolTypeOptions => PoolTypes.All;

    public class VenueForm
    {
        public string Name { get; set; } = "";
        public string? PoolType { get; set; } = PoolTypes.Short;
        public bool IsAmbiguous { get; set; }
        public string? Note { get; set; }
        public string? Aliases { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (IsNew)
        {
            Input = new VenueForm { Name = Venue?.Trim() ?? "" };
            return Page();
        }

        Existing = await repo.GetByIdAsync(Id!.Value, ct);
        if (Existing is null) return RedirectToPage("Index");
        Input = new VenueForm
        {
            Name = Existing.Name,
            PoolType = Existing.PoolType,
            IsAmbiguous = Existing.IsAmbiguous,
            Note = Existing.Note,
            Aliases = string.Join("\n", Existing.Aliases),
        };
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var input = new VenuePoolInputDto(Input.Name, Input.PoolType, Input.IsAmbiguous, Input.Note, Input.Aliases);
        var confirmedBy = User.Identity?.Name ?? "admin";
        var result = IsNew
            ? await repo.CreateAsync(input, confirmedBy, ct)
            : await repo.UpdateAsync(Id!.Value, input, confirmedBy, ct);

        if (!result.Success)
        {
            Error = result.Error;
            if (!IsNew) Existing = await repo.GetByIdAsync(Id!.Value, ct);
            return Page();
        }

        var saved = await repo.GetByIdAsync(result.Id, ct);
        var what = saved is null ? Input.Name.Trim()
            : $"«{saved.Name}» → {(saved.IsAmbiguous ? "двусмысленное" : saved.PoolType)}, написаний: {saved.Aliases.Count + 1}";
        await audit.LogAsync(IsNew ? "venuepool.create" : "venuepool.update", "VenuePool",
            result.Id.ToString(), (IsNew ? "Добавлено место " : "Место изменено: ") + what);

        TempData["Flash"] = IsNew ? "Место добавлено" : "Изменения сохранены";
        return RedirectToPage("Edit", new { id = result.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (IsNew) return RedirectToPage("Index");

        var name = (await repo.GetByIdAsync(Id!.Value, ct))?.Name;
        var result = await repo.DeleteAsync(Id!.Value, ct);
        if (!result.Success)
        {
            Error = result.Error;
            return Page();
        }

        await audit.LogAsync("venuepool.delete", "VenuePool", Id!.Value.ToString(), $"Удалено место «{name}»");
        TempData["Flash"] = "Место удалено";
        return RedirectToPage("Index");
    }
}
