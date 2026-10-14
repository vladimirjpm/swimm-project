namespace Swimm.Application.Abstractions;

/// <summary>
/// Отправка транзакционных писем (подтверждение email, сброс пароля).
/// Дев-реализация логирует ссылку; прод подключает реальный SMTP/провайдер.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);

    /// <summary>
    /// Куда на самом деле уходят письма этого процесса — для блока «Почта» в админке
    /// (/Admin/Settings, строка на дашборде). Без секретов. Дефолт — для тестовых заглушек.
    /// </summary>
    EmailTransportInfo Transport => EmailTransportInfo.Unknown;
}

/// <summary>Способ доставки писем, выбранный по конфигу при старте (см. DependencyInjection).</summary>
public enum EmailTransportKind
{
    /// <summary>Реализация не сообщила (тестовая заглушка).</summary>
    Unknown,

    /// <summary>Настоящий SMTP (Resend): письма уходят адресату.</summary>
    Smtp,

    /// <summary>SMTP не настроен, Development: письмо со ссылкой печатается в консоль — штатно.</summary>
    DevLog,

    /// <summary>SMTP не настроен вне Development: письма не уходят никуда — отказ конфигурации.</summary>
    Disabled,
}

/// <param name="Kind">Способ доставки.</param>
/// <param name="Host">SMTP-хост (только для <see cref="EmailTransportKind.Smtp"/>).</param>
/// <param name="Port">SMTP-порт.</param>
/// <param name="From">Адрес отправителя.</param>
/// <param name="FromName">Имя отправителя.</param>
public sealed record EmailTransportInfo(
    EmailTransportKind Kind,
    string? Host = null,
    int? Port = null,
    string? From = null,
    string? FromName = null)
{
    public static readonly EmailTransportInfo Unknown = new(EmailTransportKind.Unknown);

    /// <summary>Письма реально доходят до людей (или это дев, где так и задумано).</summary>
    public bool IsHealthy => Kind is EmailTransportKind.Smtp or EmailTransportKind.DevLog;
}
