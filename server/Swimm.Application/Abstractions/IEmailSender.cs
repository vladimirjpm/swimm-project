namespace Swimm.Application.Abstractions;

/// <summary>
/// Отправка транзакционных писем (подтверждение email, сброс пароля).
/// Дев-реализация логирует ссылку; прод подключает реальный SMTP/провайдер.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
