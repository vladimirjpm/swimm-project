using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Дев-реализация <see cref="IEmailSender"/>: не отправляет письмо, а логирует его (включая ссылку).
/// Для production заменить на SMTP/провайдер (SendGrid, Postmark и т. п.), зарегистрировав другую
/// реализацию в DI. Тело письма логируется на Information — удобно забирать ссылку из консоли в деве.
/// </summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DEV EMAIL] To: {To}\nSubject: {Subject}\nBody:\n{Body}",
            toEmail, subject, htmlBody);
        return Task.CompletedTask;
    }
}
