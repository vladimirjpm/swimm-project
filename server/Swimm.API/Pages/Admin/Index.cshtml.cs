using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;

namespace Swimm.API.Pages.Admin;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IEmailSender _email;

    public IndexModel(IEmailSender email) => _email = email;

    /// <summary>Пункт «Почта» системной строки (из конфига процесса, не из БД).</summary>
    public EmailTransportInfo EmailTransport => _email.Transport;

    public void OnGet() { }
}
