using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;

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

    /// <summary>Канонические типы бассейна для select-а (парсер может определить сам — «—» остаётся).</summary>
    public IReadOnlyList<string> PoolTypeOptions => PoolTypes.All;

    /// <summary>Опция DDL языка: код + подпись. Страны — общий каталог <see cref="Shared.CountryCatalog"/>.</summary>
    public record SelectOption(string Code, string Label);

    /// <summary>Языки протокола: he/en — единственные, которые понимает парсер isr.org.il.</summary>
    public static readonly IReadOnlyList<SelectOption> Languages =
    [
        new("he", "HE — иврит"),
        new("en", "EN — английский")
    ];

    public void OnGet()
    {
        ParseFormats = _sourceProvider.AvailableFormats;
    }
}
