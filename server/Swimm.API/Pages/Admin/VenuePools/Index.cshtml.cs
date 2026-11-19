using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.VenuePools;

/// <summary>
/// Справочник «место проведения → длина бассейна» (П4, docs/admin-pages/venuepools.md).
/// Список мест + места «Входящих», которых справочник не знает. CRUD — на странице Edit.
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel(IVenuePoolAdminRepository repo) : PageModel
{
    public IReadOnlyList<VenuePoolRowDto> Pools { get; private set; } = [];
    public IReadOnlyList<UnknownVenueDto> Unknown { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Pools = await repo.GetAllAsync(ct);
        Unknown = await repo.GetUnknownVenuesAsync(ct);
    }
}
