using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Дев-реализация <see cref="IEmailSender"/>: не отправляет письмо, а логирует его.
/// Выбирается автоматически, когда пуст <c>Email:Smtp:Host</c> (см. DependencyInjection);
/// для production достаточно задать <c>Email__Smtp__Host</c> — тогда встанет SmtpEmailSender.
/// </summary>
/// <remarks>
/// ⚠ Тело письма попадает в лог, только если <paramref name="logBody"/> = true (Development).
/// В теле — одноразовые токены подтверждения почты и сброса пароля, а логи App Service доступны
/// шире, чем консоль разработчика. В деплое попадание сюда означает отказ конфигурации:
/// письма не уходят вообще, поэтому уровень Error, а не Information.
/// docs/plans/azure-deploy-plan.md Б14.
/// </remarks>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;
    private readonly bool _logBody;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger, bool logBody = false)
    {
        _logger = logger;
        _logBody = logBody;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (_logBody)
        {
            // В деве ссылку забирают из консоли — штатный способ пройти регистрацию и
            // сброс пароля без настоящего SMTP.
            _logger.LogInformation(
                "[DEV EMAIL] To: {To}\nSubject: {Subject}\nBody:\n{Body}",
                toEmail, subject, htmlBody);
        }
        else
        {
            // Ни тела, ни ссылки: токен одноразовый и даёт доступ к аккаунту.
            _logger.LogError(
                "Письмо НЕ отправлено — SMTP не настроен (пуст Email__Smtp__Host). To: {To}, Subject: {Subject}",
                toEmail, subject);
        }

        return Task.CompletedTask;
    }
}
