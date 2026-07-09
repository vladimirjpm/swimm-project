using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;

namespace Swimm.API.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ImportModel : PageModel
{
    private readonly IResultSourceProvider _sourceProvider;

    public ImportModel(IResultSourceProvider sourceProvider)
    {
        _sourceProvider = sourceProvider;
    }

    public IReadOnlyList<string> ParseFormats { get; private set; } = [];

    public void OnGet()
    {
        ParseFormats = _sourceProvider.AvailableFormats;
    }
}
