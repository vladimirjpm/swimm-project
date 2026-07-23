using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Styles;

/// <summary>Справочник стилей плавания (фаза 7.2 A). CRUD — на странице Edit.</summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IStyleAdminRepository _repo;

    public IndexModel(IStyleAdminRepository repo) => _repo = repo;

    public IReadOnlyList<StyleAdminRowDto> Styles { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Styles = await _repo.GetAllAsync();
    }
}
