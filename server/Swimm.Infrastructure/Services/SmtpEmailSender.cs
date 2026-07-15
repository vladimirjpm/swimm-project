using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swimm.Application.Abstractions;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Настройки SMTP-отправки (секция конфигурации "Email:Smtp").
/// Секреты (User/Password) задаются только через env (Email__Smtp__Password) или
/// user-secrets — НЕ в appsettings.json в репозитории. Пустой Host = SMTP не настроен,
/// DI оставляет <see cref="LoggingEmailSender"/> (дев-поведение).
/// </summary>
public class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    /// <summary>STARTTLS на 587 (по умолчанию). Для implicit TLS (465) SmtpClient не подходит.</summary>
    public bool EnableSsl { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>Адрес отправителя; если пуст — берётся User.</summary>
    public string From { get; set; } = "";
    public string FromName { get; set; } = "SwimHub";
}

/// <summary>
/// Прод-реализация <see cref="IEmailSender"/>: транзакционные письма через SMTP
/// (годится для Resend/Postmark/SendGrid и обычного почтового хостинга — все дают SMTP-endpoint).
/// Ошибка отправки логируется и пробрасывается — вызывающий код (LocalAuthService) сам решает,
/// что отвечать пользователю.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpEmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpEmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var from = string.IsNullOrWhiteSpace(_options.From) ? _options.User : _options.From;

        using var message = new MailMessage
        {
            From = new MailAddress(from, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        message.To.Add(toEmail);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Credentials = string.IsNullOrEmpty(_options.User)
                ? null
                : new NetworkCredential(_options.User, _options.Password),
        };

        try
        {
            await client.SendMailAsync(message, ct);
            _logger.LogInformation("Email sent to {To}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            // Адресат — PII, тело (с токен-ссылкой) не логируем никогда.
            _logger.LogError(ex, "Failed to send email to {To}: {Subject}", toEmail, subject);
            throw;
        }
    }
}
