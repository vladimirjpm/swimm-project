using Swimm.Application.Abstractions;

namespace Swimm.API.Pages.Admin.Shared;

/// <summary>
/// Тексты блока «Почта» — одно место на карточку /Admin/Settings#email и строку дашборда.
/// Зачем блок: письма о заявках клубов шлёт ЛОКАЛЬНАЯ админка (ProdAdmin, решение В17), и без
/// <c>Email:Smtp</c> в её конфиге они молча уходят в лог. Блок показывает это сразу.
/// </summary>
public static class EmailStatusView
{
    /// <summary>Коротко — для системной строки дашборда.</summary>
    public static string ShortLabel(EmailTransportInfo t) => t.Kind switch
    {
        EmailTransportKind.Smtp => $"SMTP · {t.Host}",
        EmailTransportKind.DevLog => "в консоль (dev)",
        EmailTransportKind.Disabled => "⚠ не отправляются",
        _ => "неизвестно",
    };

    /// <summary>Пояснение для карточки на /Admin/Settings.</summary>
    public static string Detail(EmailTransportInfo t) => t.Kind switch
    {
        EmailTransportKind.Smtp =>
            $"Письма уходят через {t.Host}:{t.Port} от {t.FromName} <{t.From}>. " +
            "Доставку, отказы и спам-жалобы по каждому письму показывает панель Resend (Emails, Logs).",
        EmailTransportKind.DevLog =>
            "SMTP не настроен — это Development: письмо со ссылкой печатается в консоль API, наружу не уходит. " +
            "Живая проверка — запустить API с переменными Email__Smtp__* (docs/plans/azure-deploy-handoff.md §3.2).",
        EmailTransportKind.Disabled =>
            "SMTP не настроен (пуст Email:Smtp:Host): письма не уходят никуда, в лог пишется только факт. " +
            "В ProdAdmin так пропадают письма о заявках клубов. Хост и отправитель — appsettings.ProdAdmin.json, " +
            "ключ Resend — appsettings.ProdAdmin.Local.json (Email:Smtp:Password).",
        _ => "Реализация отправки не сообщила, куда уходят письма.",
    };

    /// <summary>Тестовое письмо имеет смысл: настоящий SMTP или дев-консоль.</summary>
    public static bool CanSendTest(EmailTransportInfo t) => t.IsHealthy;
}
