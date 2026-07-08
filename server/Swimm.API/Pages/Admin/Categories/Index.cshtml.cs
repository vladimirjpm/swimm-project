using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Categories;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly ICategoryAdminRepository _repo;

    public IndexModel(ICategoryAdminRepository repo) => _repo = repo;

    public IReadOnlyList<CategoryAdminRowDto> Categories { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Categories = await _repo.GetAllAsync();
    }
}
