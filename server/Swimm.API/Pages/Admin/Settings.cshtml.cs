using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.API.Pages.Admin.Shared;
using Swimm.Application.Abstractions;

namespace Swimm.API.Pages.Admin;

[Authorize(Roles = "Admin")]
public class SettingsModel : PageModel
{
    private readonly IEmailSender _email;
    private readonly ICurrentActor _actor;
    private readonly IAdminAuditService _audit;

    public SettingsModel(IEmailSender email, ICurrentActor actor, IAdminAuditService audit)
    {
        _email = email;
        _actor = actor;
        _audit = audit;
    }

    /// <summary>Блок «Почта»: куда уходят письма этого процесса.</summary>
    public EmailTransportInfo EmailTransport => _email.Transport;

    /// <summary>Адрес вошедшего админа — тестовое письмо уходит только ему.</summary>
    public string? AdminEmail => _actor.Name.Contains('@') ? _actor.Name : null;

    /// <summary>Итог последней тестовой отправки (PRG через TempData).</summary>
    [TempData] public string? EmailTestResult { get; set; }
    [TempData] public bool EmailTestFailed { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostTestEmailAsync(CancellationToken ct)
    {
        var transport = _email.Transport;
        var to = AdminEmail;

        if (to == null)
            Report(failed: true, "У вошедшего аккаунта нет email — некуда слать.");
        else if (!EmailStatusView.CanSendTest(transport))
            Report(failed: true, "Не отправлено: SMTP не настроен, письмо ушло бы только в лог.");
        else
        {
            try
            {
                await _email.SendAsync(to, "SwimHub — test email",
                    $"Test email from the SwimHub admin panel.<br>Sent {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC " +
                    $"via {(transport.Kind == EmailTransportKind.Smtp ? transport.Host : "dev console")}.", ct);

                Report(failed: false, transport.Kind == EmailTransportKind.Smtp
                    ? $"Отправлено на {to}. Проверь ящик (и «Спам»); статус доставки — в панели Resend."
                    : $"Напечатано в консоли API (Development) — наружу не ушло.");
            }
            catch (Exception ex)
            {
                // Текст ошибки SMTP — то, ради чего кнопка: неверный ключ, порт, отправитель.
                Report(failed: true, $"Ошибка отправки: {ex.GetBaseException().Message}");
            }

            await _audit.LogAsync("email.test", "Email", summary:
                $"Тестовое письмо на {to}: {(EmailTestFailed ? "ошибка" : "OK")}", ct: ct);
        }

        return RedirectToPage(pageName: null, pageHandler: null, routeValues: null, fragment: "email");
    }

    private void Report(bool failed, string text)
    {
        EmailTestFailed = failed;
        EmailTestResult = text;
    }
}
